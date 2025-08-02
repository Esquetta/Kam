using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Commands;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;

public class CommandHandlerService : ICommandHandlerService
{
    private readonly IMediator _mediator;
    private readonly Dictionary<CommandType, Func<DynamicCommandRequest, Task<object>>> _commandMappings;

    public CommandHandlerService(IMediator mediator)
    {
        _mediator = mediator;
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
                ExtractAppNameFromText(req.OriginalText)
            ),

            [CommandType.SendMessage] = async (req) => new SendMessageCommand(
                ExtractEntity(req.Entities, "recipient") ?? "Unknown",
                ExtractEntity(req.Entities, "message") ?? req.OriginalText
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
            ),
            [CommandType.CloseApplication]=async (req) => new CloseApplicationCommand(
                ExtractEntity(req.Entities, "applicationName") ??
                ExtractEntity(req.Entities, "app_name") ??
                ExtractAppNameFromText(req.OriginalText)
            )
        };
    }

    private async Task<CommandResult> HandleUnknownIntent(DynamicCommandRequest request)
    {
        return new CommandResult
        {
            Success = false,
            Message = $"'{request.Intent}' komutu henüz desteklenmiyor.",
            Data = new
            {
                suggestion = "GetAvailableCommandsAsync fonksiyonunu kullanarak desteklenen komutları görebilirsiniz.",
                detectedIntent = request.Intent,
                entities = request.Entities
            },
            OriginalInput = request.OriginalText
        };
    }

    // Entity extraction helpers
    private string ExtractEntity(Dictionary<string, object> entities, string key)
    {
        return entities?.TryGetValue(key, out var value) == true ? value?.ToString() : null;
    }

    private string ExtractAppNameFromText(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("chrome")) return "chrome";
        if (lower.Contains("notepad") || lower.Contains("not defteri")) return "notepad";
        if (lower.Contains("calculator") || lower.Contains("hesap makinesi")) return "calculator";
        if (lower.Contains("word")) return "winword";
        if (lower.Contains("excel")) return "excel";
        return "chrome"; // Default fallback
    }

    private string ExtractMusicFromText(string text)
    {
        // Basit music extraction - gerçek implementasyonda daha sofistike olabilir
        return text.Contains("müzik") ? "default_music" : text;
    }

    private string ExtractQueryFromText(string text)
    {
        var lower = text.ToLowerInvariant();
        // Remove common command words
        var cleanedText = lower
            .Replace("ara", "")
            .Replace("aratır", "")
            .Replace("search", "")
            .Replace("google", "")
            .Replace("web", "")
            .Trim();

        return string.IsNullOrEmpty(cleanedText) ? text : cleanedText;
    }

    private string ExtractDeviceFromText(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("bluetooth")) return "bluetooth";
        if (lower.Contains("wifi") || lower.Contains("wi-fi")) return "wifi";
        if (lower.Contains("ekran") || lower.Contains("screen")) return "screen";
        if (lower.Contains("ses") || lower.Contains("volume")) return "volume";
        return "unknown_device";
    }

    private string ExtractActionFromText(string text)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("aç") || lower.Contains("open") || lower.Contains("başlat")) return "open";
        if (lower.Contains("kapat") || lower.Contains("close") || lower.Contains("durdur")) return "close";
        if (lower.Contains("artır") || lower.Contains("yükselt") || lower.Contains("increase")) return "increase";
        if (lower.Contains("azalt") || lower.Contains("düşür") || lower.Contains("decrease")) return "decrease";
        return "toggle";
    }

    // Localization helpers
    private string GetCommandDescription(CommandType commandType, string language)
    {
        var descriptions = new Dictionary<CommandType, Dictionary<string, string>>
        {
            [CommandType.OpenApplication] = new()
            {
                ["tr"] = "Uygulama açar",
                ["en"] = "Opens application"
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
                ["tr"] = new() { "Chrome aç", "Chrome açar mısın?", "Lütfen Notepad başlat" },
                ["en"] = new() { "Open Chrome", "Can you open Chrome?", "Please start Notepad" }
            },
            [CommandType.PlayMusic] = new()
            {
                ["tr"] = new() { "Müzik çal", "Şarkı başlat", "Müzik çalar mısın?" },
                ["en"] = new() { "Play music", "Start song", "Can you play music?" }
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