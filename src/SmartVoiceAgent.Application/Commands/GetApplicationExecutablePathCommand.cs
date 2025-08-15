using MediatR;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Application.Pipelines.Performance;

namespace SmartVoiceAgent.Application.Commands
{
    public record GetApplicationExecutablePathCommand(string ApplicationName) : IRequest<string>, ICachableRequest, IIntervalRequest
    {
        // Caching
        public string CacheKey => $"GetApplicationExecutablePath-{ApplicationName}";
        public string? CacheGroupKey => "GetApplicationExecutablePath";
        public bool BypassCache => false;
        public TimeSpan? SlidingExpiration => TimeSpan.FromMinutes(5);

        // Performance
        public bool EnablePerformanceLogging => true;

        public int Interval => 2;
    }
}
