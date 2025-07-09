using Core.CrossCuttingConcerns.Logging.Serilog;
using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Core.Config;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Keyword-based intent detection service.
/// </summary>
public class IntentDetectorService : IIntentDetectionService
{
    private readonly LoggerServiceBase _logger;
    private readonly IntentConfig _config;
    private readonly Dictionary<string, List<IntentPattern>> _intentPatterns;
    private readonly Dictionary<string, Regex> _entityRegexes;

    public IntentDetectorService(LoggerServiceBase logger, IConfiguration configuration)
    {
        _logger = logger;
        _config = configuration.GetSection("Intent").Get<IntentConfig>() ?? throw new NullReferenceException($"Intent section cannot found in configuration."); ;
        _intentPatterns = LoadIntentPatterns();
        _entityRegexes = LoadEntityRegexes();
    }

    public async Task<IntentResult> DetectIntentAsync(string text, string language, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken); // async context

        if (string.IsNullOrWhiteSpace(text))
        {
            return new IntentResult { Intent = CommandType.Unknown };
        }

        try
        {
            var normalizedText = NormalizeText(text);
            var patterns = GetPatternsForLanguage(language);

            var matches = patterns
                .Select(pattern => new
                {
                    Intent = pattern.Intent,
                    Pattern = pattern,
                    Score = CalculateScore(normalizedText, pattern)
                })
                .Where(x => x.Score >= _config.MinimumConfidence)
                .OrderByDescending(x => x.Score)
                .ToList();

            if (!matches.Any())
            {
                return new IntentResult
                {
                    Intent = CommandType.Unknown,
                    OriginalText = text,
                    Language = language
                };
            }

            var bestMatch = matches.First();
            var entities = await ExtractEntitiesAsync(text, bestMatch.Intent, language);

            return new IntentResult
            {
                Intent = bestMatch.Intent,
                Confidence = bestMatch.Score,
                Entities = entities,
                Language = language,
                OriginalText = text
            };
        }
        catch (Exception ex)
        {
            _logger.Error($"Intent detection failed: {ex.Message}");
            return new IntentResult
            {
                Intent = CommandType.Unknown,
                OriginalText = text,
                Language = language
            };
        }
    }

    private Dictionary<string, List<IntentPattern>> LoadIntentPatterns()
    {
        return new Dictionary<string, List<IntentPattern>>
        {
            ["en"] = new List<IntentPattern>
            {
                new IntentPattern(CommandType.OpenApplication, new[] { "open", "start", "launch" }),
                new IntentPattern(CommandType.PlayMusic, new[] { "music", "song", "play" }),
                new IntentPattern(CommandType.SendMessage, new[] { "message", "send", "sms" }),
                new IntentPattern(CommandType.SearchWeb, new[] { "search", "google", "find" }),
            },
            ["tr"] = new List<IntentPattern>
            {
                new IntentPattern(CommandType.OpenApplication, new[] { "aç", "başlat", "çalıştır" }),
                new IntentPattern(CommandType.PlayMusic, new[] { "müzik", "şarkı", "çal" }),
                new IntentPattern(CommandType.SendMessage, new[] { "mesaj", "gönder" }),
                new IntentPattern(CommandType.SearchWeb, new[] { "ara", "bul", "google" }),
            }
        };
    }

    private Dictionary<string, Regex> LoadEntityRegexes()
    {
        return new Dictionary<string, Regex>
        {
            ["time"] = new Regex(@"\b\d{1,2}:\d{2}\b"),
            ["date"] = new Regex(@"\b\d{1,2}/\d{1,2}/\d{4}\b"),
            ["number"] = new Regex(@"\b\d+\b")
        };
    }

    private string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        
        var normalizedText = Regex.Replace(text, @"[^\w\s\u00C0-\u017F]", " ");

        
        normalizedText = Regex.Replace(normalizedText, @"\s+", " ");

        
        //normalizedText = NormalizeTurkishChars(normalizedText);

        return normalizedText.ToLowerInvariant().Trim();
    }
    private string NormalizeTurkishChars(string text)
    {
        
        var turkishChars = new Dictionary<char, char>
        {
            {'ç', 'c'}, {'ğ', 'g'}, {'ı', 'i'}, {'ö', 'o'}, {'ş', 's'}, {'ü', 'u'},
            {'Ç', 'C'}, {'Ğ', 'G'}, {'İ', 'I'}, {'Ö', 'O'}, {'Ş', 'S'}, {'Ü', 'U'}
        };

        var sb = new StringBuilder();
        foreach (char c in text)
        {
            sb.Append(turkishChars.ContainsKey(c) ? turkishChars[c] : c);
        }
        return sb.ToString();
    }

    private List<IntentPattern> GetPatternsForLanguage(string language)
    {
        return _intentPatterns.ContainsKey(language)
            ? _intentPatterns[language]
            : _intentPatterns["en"];
    }

    private float CalculateScore(string text, IntentPattern pattern)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matchCount = pattern.Keywords.Count(keyword => words.Contains(keyword));
        return (float)matchCount / pattern.Keywords.Length * pattern.Weight;
    }

    private async Task<Dictionary<string, object>> ExtractEntitiesAsync(string text, CommandType intent, string language)
    {
        await Task.Delay(1);
        var entities = new Dictionary<string, object>();

        foreach (var kvp in _entityRegexes)
        {
            var matches = kvp.Value.Matches(text);
            if (matches.Any())
            {
                entities[kvp.Key] = matches.Select(m => m.Value).ToArray();
            }
        }

        return entities;
    }
}
