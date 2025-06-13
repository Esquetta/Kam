using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Application.Pipelines.Performance;
using SmartVoiceAgent.Core.Contracts;
using SmartVoiceAgent.Core.Enums;

namespace SmartVoiceAgent.Application.Queries;

/// <summary>
/// Query for getting the status of an application.
/// </summary>
public record GetApplicationStatusQuery(string ApplicationName)
    : IQuery<AppStatus>, ICachableRequest, IIntervalRequest
{
    // Caching
    public string CacheKey => $"ApplicationStatus-{ApplicationName}";
    public string? CacheGroupKey => null;
    public bool BypassCache => false;
    public TimeSpan? SlidingExpiration => TimeSpan.FromMinutes(5);

    // Performance
    public bool EnablePerformanceLogging => true;

    public int Interval => 1;
}




