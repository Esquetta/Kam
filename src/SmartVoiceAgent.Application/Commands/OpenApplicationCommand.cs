using MediatR;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Application.Pipelines.Performance;

namespace SmartVoiceAgent.Core.Commands;

/// <summary>
/// Command to open an application.
/// </summary>
/// <param name="ApplicationName">The name of the application to open.</param>
public record OpenApplicationCommand(string ApplicationName)
    : IRequest<CommandResultDTO>, ICachableRequest, IIntervalRequest
{
    // Caching
    public string CacheKey => $"OpenApplication-{ApplicationName}";
    public string? CacheGroupKey => "ApplicationCommands";
    public bool BypassCache => false;
    public TimeSpan? SlidingExpiration => TimeSpan.FromMinutes(5);

    // Performance
    public bool EnablePerformanceLogging => true;

    public int Interval => 2;
}



