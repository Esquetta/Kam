using MediatR;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Application.Pipelines.Performance;

namespace SmartVoiceAgent.Application.Commands;

public record SendMessageCommand(string Recipient, string Message)
    : IRequest<CommandResultDTO>, ICachableRequest, IIntervalRequest
{
    // Caching
    public string CacheKey => $"SendMessage-{Recipient}";
    public string? CacheGroupKey => null;
    public bool BypassCache => false;
    public TimeSpan? SlidingExpiration => TimeSpan.FromMinutes(5);

    // Performance
    public bool EnablePerformanceLogging => true;

    public int Interval => 1;
}
