using MediatR;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Application.Pipelines.Performance;

namespace SmartVoiceAgent.Application.Commands;

public record ControlDeviceCommand(string DeviceName, string Action)
    : IRequest<CommandResultDTO>, ICachableRequest, IIntervalRequest
{
    // Caching
    public string CacheKey => $"ControlDevice-{DeviceName}-{Action}";
    public string? CacheGroupKey => "DeviceCommands";
    public bool BypassCache => false;
    public TimeSpan? SlidingExpiration => TimeSpan.FromMinutes(3);

    // Performance
    public bool EnablePerformanceLogging => true;

    public int Interval => 2;
}


