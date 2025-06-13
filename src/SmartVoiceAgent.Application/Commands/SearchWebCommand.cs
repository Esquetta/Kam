using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Application.Pipelines.Performance;
using SmartVoiceAgent.Core.Contracts;

namespace SmartVoiceAgent.Application.Commands;
public record SearchWebCommand(string Query)
    : ICommand<CommandResultDTO>, ICachableRequest, IIntervalRequest
{
    // Caching
    public string CacheKey => $"SearchWeb-{Query}";
    public string? CacheGroupKey => "SearchCommands";
    public bool BypassCache => false;
    public TimeSpan? SlidingExpiration => TimeSpan.FromMinutes(3);

    // Performance
    public bool EnablePerformanceLogging => true;

    public int Interval => 2;
}


