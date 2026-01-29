using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using System.Text.RegularExpressions;

public class CommandHandlerService : ICommandHandlerService
{
    private readonly IMediator _mediator;
    private readonly DynamicAppExtractionService _appExtractionService;
    private readonly Dictionary<CommandType, Func<DynamicCommandRequest, Task<object>>> _commandMappings;

    // Pre-compiled regex patterns for performance
    private static readonly Regex[] MusicPatterns = new[]
    {
        new Regex(@"çal\s+(.+?)(?:\s+şarkısını|\s+müziğini|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"play\s+(.+?)(?:\s+song|\s+music|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"(.+?)\s+çal", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new Regex(@"(.+?)\s+play", RegexOptions.Compiled | RegexOptions.IgnoreCase)
    };

    private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
    
    private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ara", "aratır", "search", "google", "web", "internet", "bul", "find"
    };

    private static readonly Dictionary<string, string[]> DeviceMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bluetooth"] = new[] { "bluetooth", "bt" },
        ["wifi"] = new[] { "wifi", "wi-fi", "internet", "ağ" },
        ["screen"] = new[] { "ekran", "screen", "monitor", "display" },
        ["volume"] = new[] { "ses", "volume", "sesli", "hoparlör", "speaker" },
        ["microphone"] = new[] { "mikrofon", "microphone", "mic" },
        ["camera"] = new[] { "kamera", "camera", "webcam" },
        ["keyboard"] = new[] { "klavye", "keyboard" },
        ["mouse"] = new[] { "fare", "mouse" }
    };

    private static readonly Dictionary<string, string[]> ActionMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["open"] = new[] { "aç", "open", "başlat", "start", "çalıştır", "run" },
        ["close"] = new[] { "kapat", "close", "durdur", "stop", "sonlandır", "end" },
        ["increase"] = new[] { "artır", "yükselt", "increase", "up", "arttır" },
        ["decrease"] = new[] { "azalt", "düşür", "decrease", "down", "alçalt" },
        ["mute"] = new[] { "sustur", "mute", "sessiz" },
        ["unmute"] = new[] { "sesli", "unmute", "aç" },
        ["enable"] = new[] { "etkinleştir", "enable", "açık" },
        ["disable"] = new[] { "devre dışı", "disable", "kapalı" }
    };

    private static readonly Regex RecipientPattern1 = new Regex(@"(?:to|için)\s+(.+?)(?:\s+mesaj|\s+message|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RecipientPattern2 = new Regex(@"(.+?)(?:\s+için|\s+to)\s+mesaj", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RecipientPattern3 = new Regex(@"(.+?)(?:'ye|'ya|'e|'a)\s+mesaj", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex[] RecipientPatterns = new[] { RecipientPattern1, RecipientPattern2, RecipientPattern3 };

    private static readonly HashSet<string> CommandWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mesaj", "message", "gönder", "send", "yaz", "write"
    };

    public CommandHandlerService(IMediator mediator, IApplicationServiceFactory applicationServiceFactory)
    {
        _mediator = mediator;
        _appExtractionService = new DynamicAppExtractionService(applicationServiceFactory);
        _commandMappings = InitializeCommandMappings();
    }

    // Constructor with dependency injection for the app extraction service
    public CommandHandlerService(IMediator mediator, DynamicAppExtractionService appExtractionService)
    {
        _mediator = mediator;
        _appExtractionService = appExtractionService;
        _commandMappings = InitializeCommandMappings();
    }

    public async Task<CommandResult> ExecuteCommandAsync(DynamicCommandRequest request)
    {
        try
        {
            if (Enum.TryParse<CommandType>(request.Intent, true, out var commandType) &&
                _commandMappings.TryGetValue(commandType, out var commandFactory))
            {
                var command = await commandFactory(request);
                var result = await _mediator.Send(command);

                return new CommandResult
                {
                    Success = true,
                    Message = $"{commandType} komutu başarıyla çalıştırıldı",
                    Data = result,
                    OriginalInput = request.OriginalText
                };
            }

            // Fallback: Handle unknown intents
            return await HandleUnknownIntent(request);
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                Success = false,
                Message = $"Komut çalıştırılırken hata: {ex.Message}",
                Error = ex.Message,
                OriginalInput = request.OriginalText
            };
        }
    }

    public async Task<List<AvailableCommand>> GetAvailableCommandsAsync(string language, string category = null)
    {
        var commands = new List<AvailableCommand>();

        foreach (var mapping in _commandMappings)
        {
            var command = new AvailableCommand
            {
                Intent = mapping.Key.ToString(),
                Description = GetCommandDescription(mapping.Key, language),
                Examples = GetCommandExamples(mapping.Key, language),
                Category = GetCommandCategory(mapping.Key)
            };

            if (string.IsNullOrEmpty(category) || command.Category == category)
            {
                commands.Add(command);
            }
        }

        return commands;
    }

    private Dictionary<CommandType, Func<DynamicCommandRequest, Task<object>>> InitializeCommandMappings()
    {
        return new Dictionary<CommandType, Func<DynamicCommandRequest, Task<object>>>
        {
            [CommandType.OpenApplication] = async (req) => new OpenApplicationCommand(
                ExtractEntity(req.Entities, "applicationName") ??
                ExtractEntity(req.Entities, "app_name") ??
                await ExtractAppNameFromTextAsync(req.OriginalText)
            ),

            [CommandType.CloseApplication] = async (req) => new CloseApplicationCommand(
                ExtractEntity(req.Entities, "applicationName") ??
                ExtractEntity(req.Entities, "app_name") ??
                await ExtractAppNameFromTextAsync(req.OriginalText)
            ),

            [CommandType.SendMessage] = async (req) => new SendMessageCommand(
                ExtractEntity(req.Entities, "recipient") ??
                ExtractRecipientFromText(req.OriginalText) ?? "Unknown",
                ExtractEntity(req.Entities, "message") ??
                ExtractMessageFromText(req.OriginalText) ?? req.OriginalText
            ),

            [CommandType.PlayMusic] = async (req) => new PlayMusicCommand(
                ExtractEntity(req.Entities, "trackName") ??
                ExtractEntity(req.Entities, "song_name") ??
                ExtractMusicFromText(req.OriginalText)
            ),

            [CommandType.SearchWeb] = async (req) => new SearchWebCommand(
                ExtractEntity(req.Entities, "searchQuery") ??
                ExtractEntity(req.Entities, "query") ??
                ExtractQueryFromText(req.OriginalText),
                req.Language,
                int.Parse(ExtractEntity(req.Entities, "resultCount") ?? "5")
            ),

            [CommandType.ControlDevice] = async (req) => new ControlDeviceCommand(
                ExtractEntity(req.Entities, "deviceName") ?? ExtractDeviceFromText(req.OriginalText),
                ExtractEntity(req.Entities, "action") ?? ExtractActionFromText(req.OriginalText)
            )
        };
    }

    private async Task<CommandResult> HandleUnknownIntent(DynamicCommandRequest request)
    {
        // Try to suggest similar commands or provide helpful information
        var suggestions = await GetSuggestionsForUnknownIntent(request);

        return new CommandResult
        {
            Success = false,
            Message = $"'{request.Intent}' komutu henüz desteklenmiyor.",
            Data = new
            {
                suggestion = "GetAvailableCommandsAsync fonksiyonunu kullanarak desteklenen komutları görebilirsiniz.",
                detectedIntent = request.Intent,
                entities = request.Entities,
                possibleCommands = suggestions
            },
            OriginalInput = request.OriginalText
        };
    }

    // Enhanced entity extraction helpers
    private string ExtractEntity(Dictionary<string, object> entities, string key)
    {
        return entities?.TryGetValue(key, out var value) == true ? value?.ToString() : null;
    }

    // Updated application name extraction using the enhanced service
    private async Task<string> ExtractAppNameFromTextAsync(string text)
    {
        return await _appExtractionService.ExtractApplicationNameAsync(text);
    }

    private string ExtractMusicFromText(string text)
    {
        // Use pre-compiled regex patterns
        foreach (var pattern in MusicPatterns)
        {
            var match = pattern.Match(text);
            if (match.Success && match.Groups[1].Value.Trim().Length > 0)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        // If no specific pattern matches, return the original text (cleaned)
        return text.Contains("müzik") || text.Contains("music") ? "default_music" : text;
    }

    private string ExtractQueryFromText(string text)
    {
        // Remove common search command words using pre-defined HashSet
        string cleanedText = text;
        foreach (var stopWord in StopWords)
        {
            cleanedText = Regex.Replace(
                cleanedText,
                $@"\b{Regex.Escape(stopWord)}\b",
                "",
                RegexOptions.IgnoreCase
            );
        }

        // Use pre-compiled whitespace regex
        cleanedText = WhitespaceRegex.Replace(cleanedText, " ").Trim();
        return string.IsNullOrEmpty(cleanedText) ? text : cleanedText;
    }

    private string ExtractDeviceFromText(string text)
    {
        // Use static device mappings with ordinal ignore case comparison
        foreach (var device in DeviceMappings)
        {
            if (device.Value.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return device.Key;
            }
        }

        return "unknown_device";
    }

    private string ExtractActionFromText(string text)
    {
        // Use static action mappings with ordinal ignore case comparison
        foreach (var action in ActionMappings)
        {
            if (action.Value.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return action.Key;
            }
        }

        return "toggle";
    }

    private string ExtractRecipientFromText(string text)
    {
        // Use pre-compiled regex patterns
        foreach (var pattern in RecipientPatterns)
        {
            var match = pattern.Match(text);
            if (match.Success && match.Groups[1].Value.Trim().Length > 0)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private string ExtractMessageFromText(string text)
    {
        // Remove command words and extract the actual message using pre-defined HashSet
        string cleanedText = text;
        foreach (var word in CommandWords)
        {
            cleanedText = Regex.Replace(
                cleanedText,
                $@"\b{Regex.Escape(word)}\b",
                "",
                RegexOptions.IgnoreCase
            );
        }

        return cleanedText.Trim();
    }

    private async Task<List<string>> GetSuggestionsForUnknownIntent(DynamicCommandRequest request)
    {
        var suggestions = new List<string>();

        // Basic keyword matching for suggestions
        var text = request.OriginalText.ToLowerInvariant();

        if (text.Contains("aç") || text.Contains("open") || text.Contains("başlat"))
            suggestions.Add("OpenApplication");

        if (text.Contains("kapat") || text.Contains("close"))
            suggestions.Add("CloseApplication");

        if (text.Contains("müzik") || text.Contains("music") || text.Contains("çal"))
            suggestions.Add("PlayMusic");

        if (text.Contains("ara") || text.Contains("search") || text.Contains("bul"))
            suggestions.Add("SearchWeb");

        if (text.Contains("mesaj") || text.Contains("message") || text.Contains("gönder"))
            suggestions.Add("SendMessage");

        return suggestions;
    }

    // Localization helpers (keeping the existing ones)
    private string GetCommandDescription(CommandType commandType, string language)
    {
        var descriptions = new Dictionary<CommandType, Dictionary<string, string>>
        {
            [CommandType.OpenApplication] = new()
            {
                ["tr"] = "Uygulama açar",
                ["en"] = "Opens application"
            },
            [CommandType.CloseApplication] = new()
            {
                ["tr"] = "Uygulamayı kapatır",
                ["en"] = "Closes application"
            },
            [CommandType.PlayMusic] = new()
            {
                ["tr"] = "Müzik çalar",
                ["en"] = "Plays music"
            },
            [CommandType.SearchWeb] = new()
            {
                ["tr"] = "Web'de arama yapar",
                ["en"] = "Searches the web"
            },
            [CommandType.ControlDevice] = new()
            {
                ["tr"] = "Cihazları kontrol eder",
                ["en"] = "Controls devices"
            },
            [CommandType.SendMessage] = new()
            {
                ["tr"] = "Mesaj gönderir",
                ["en"] = "Sends message"
            }
        };

        return descriptions.GetValueOrDefault(commandType)?.GetValueOrDefault(language) ?? commandType.ToString();
    }

    private List<string> GetCommandExamples(CommandType commandType, string language)
    {
        var examples = new Dictionary<CommandType, Dictionary<string, List<string>>>
        {
            [CommandType.OpenApplication] = new()
            {
                ["tr"] = new() { "Chrome aç", "Spotify başlat", "Word çalıştır", "Discord aç" },
                ["en"] = new() { "Open Chrome", "Start Spotify", "Run Word", "Open Discord" }
            },
            [CommandType.CloseApplication] = new()
            {
                ["tr"] = new() { "Chrome kapat", "Spotify durdur", "Word sonlandır" },
                ["en"] = new() { "Close Chrome", "Stop Spotify", "End Word" }
            },
            [CommandType.PlayMusic] = new()
            {
                ["tr"] = new() { "Müzik çal", "Şarkı başlat", "Spotify çal" },
                ["en"] = new() { "Play music", "Start song", "Play Spotify" }
            }
        };

        return examples.GetValueOrDefault(commandType)?.GetValueOrDefault(language) ?? new List<string>();
    }

    private string GetCommandCategory(CommandType commandType)
    {
        return commandType switch
        {
            CommandType.OpenApplication => "app_control",
            CommandType.CloseApplication => "app_control",
            CommandType.PlayMusic => "media",
            CommandType.SearchWeb => "web",
            CommandType.ControlDevice => "system",
            CommandType.SendMessage => "communication",
            _ => "general"
        };
    }
}