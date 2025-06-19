using MediatR;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Application.Pipelines.Performance;

public record PlayMusicCommand(string TrackName)
    : IRequest<CommandResultDTO>,IIntervalRequest
{
    //// Caching
    //public string CacheKey => $"PlayMusic-{TrackName}";
    //public string? CacheGroupKey => "MusicCommands";
    //public bool BypassCache => false;
    //public TimeSpan? SlidingExpiration => TimeSpan.FromMinutes(10);

    // Performance
    public bool EnablePerformanceLogging => true;

    public int Interval => 3;
}


