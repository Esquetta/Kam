using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using System.Diagnostics;

namespace SmartVoiceAgent.Application.Behaviors.Performance
{
    public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly LoggerServiceBase _loggerService;

        public PerformanceBehavior(LoggerServiceBase loggerService)
        {
            _loggerService = loggerService;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            // Create a new Stopwatch instance per request to ensure thread safety
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var response = await next();
                return response;
            }
            finally
            {
                stopwatch.Stop();
                var elapsedTime = stopwatch.ElapsedMilliseconds;

                // Log performance information
                if (elapsedTime > 500) // ms cinsinden, threshold configurable yapılabilir.
                {
                    _loggerService.Warn(
                        $"Performance Alert: {typeof(TRequest).Name} took {elapsedTime} ms.");
                }
                else
                {
                    _loggerService.Info(
                        $"Performance: {typeof(TRequest).Name} took {elapsedTime} ms.");
                }
            }
        }
    }
}
