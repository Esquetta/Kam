using Core.CrossCuttingConcerns.Logging.Serilog;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Infrastructure.Services.Intent;
public class HybridIntentDetectionService : IIntentDetectionService
{
    private readonly List<(IIntentDetectionService Service, string Name, float Weight)> _detectionServices;
    private readonly LoggerServiceBase _logger;

    public HybridIntentDetectionService(
        AiIntentDetectionService aiService,
        SemanticIntentDetectionService semanticService,
        ContextAwareIntentDetectionService contextService,
        IntentDetectorService patternService,
        LoggerServiceBase logger)
    {
        _detectionServices = new List<(IIntentDetectionService, string, float)>
        {
            (aiService, "AI", 1.0f),
            (semanticService, "Semantic", 0.8f),
            (contextService, "Context", 0.9f),
            (patternService, "Pattern", 0.6f)
        };
        _logger = logger;
    }

    public async Task<IntentResult> DetectIntentAsync(string text, string language, CancellationToken cancellationToken = default)
    {
        var results = new List<(IntentResult Result, string ServiceName, float Weight)>();

        // Run all detection services in parallel with error handling
        var tasks = _detectionServices.Select(async service =>
        {
            try
            {
                var result = await service.Service.DetectIntentAsync(text, language, cancellationToken);
                return (Result: result, ServiceName: service.Name, Weight: service.Weight);
            }
            catch (Exception ex)
            {
                _logger.Error($"Intent detection failed for {service.Name}: {ex.Message}");
                return (Result: new IntentResult { Intent = CommandType.Unknown, OriginalText = text, Language = language },
                       ServiceName: service.Name, Weight: 0f);
            }
        });

        var allResults = await Task.WhenAll(tasks);
        results.AddRange(allResults.Where(r => r.Result.Intent != CommandType.Unknown && r.Weight > 0));

        if (!results.Any())
        {
            _logger.Warn($"No valid intent detected for: {text}");
            return new IntentResult { Intent = CommandType.Unknown, OriginalText = text, Language = language };
        }

        // Weighted voting system
        var intentScores = new Dictionary<CommandType, (float Score, IntentResult BestResult)>();

        foreach (var (result, serviceName, weight) in results)
        {
            var score = result.Confidence * weight;

            if (intentScores.ContainsKey(result.Intent))
            {
                var current = intentScores[result.Intent];
                intentScores[result.Intent] = (
                    current.Score + score,
                    result.Confidence > current.BestResult.Confidence ? result : current.BestResult
                );
            }
            else
            {
                intentScores[result.Intent] = (score, result);
            }
        }

        var bestIntent = intentScores.OrderByDescending(kvp => kvp.Value.Score).First();

        _logger.Info($"Hybrid detection result: {bestIntent.Key} with combined score: {bestIntent.Value.Score:F2}");

        return new IntentResult
        {
            Intent = bestIntent.Key,
            Confidence = Math.Min(bestIntent.Value.Score, 1.0f),
            Entities = bestIntent.Value.BestResult.Entities,
            Language = language,
            OriginalText = text
        };
    }
}
