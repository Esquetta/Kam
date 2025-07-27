using MediatR;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Application.Pipelines.Performance;

namespace SmartVoiceAgent.Application.Commands
{
    /// <summary>
    /// Close process given application name.
    /// </summary>
    /// <param name="ApplicationName"></param>
    public record CloseApplicationCommand(string ApplicationName):IRequest<CommandResultDTO>, ICachableRequest, IIntervalRequest
    {
        // Caching
        public string CacheKey => $"CloseApplication-{ApplicationName}";
        public string? CacheGroupKey => "ApplicationCommands";
        public bool BypassCache => false;
        public TimeSpan? SlidingExpiration => TimeSpan.FromMinutes(5);

        // Performance
        public bool EnablePerformanceLogging => true;

        public int Interval => 2;
    }
}

