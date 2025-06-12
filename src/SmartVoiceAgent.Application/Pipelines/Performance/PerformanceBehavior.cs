using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using System.Diagnostics;

namespace SmartVoiceAgent.Application.Behaviors.Performance
{
    public class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly Stopwatch _stopwatch;
        private readonly LoggerServiceBase _loggerService;

        public PerformanceBehavior(LoggerServiceBase loggerService)
        {
            _stopwatch = new Stopwatch();
            _loggerService = loggerService;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            _stopwatch.Start();

            var response = await next();

            _stopwatch.Stop();

            var elapsedTime = _stopwatch.ElapsedMilliseconds;

            // Burada threshold tanımlayıp loglama yapılabilir
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

            return response;
        }
    }
}
