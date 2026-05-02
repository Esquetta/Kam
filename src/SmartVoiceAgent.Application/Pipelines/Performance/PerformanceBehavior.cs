using Core.CrossCuttingConcerns.Logging.Serilog;
using System.Diagnostics;

namespace SmartVoiceAgent.Application.Behaviors.Performance;

public class PerformanceBehavior<TRequest, TResponse> :
    ICommandPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    private readonly LoggerServiceBase _loggerService;

    public PerformanceBehavior(LoggerServiceBase loggerService)
    {
        _loggerService = loggerService;
    }

    public Task<TResponse> Handle(TRequest request, CommandHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        return HandleCore(request, next.Invoke, cancellationToken);
    }

    private async Task<TResponse> HandleCore(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            return await next();
        }
        finally
        {
            stopwatch.Stop();
            var elapsedTime = stopwatch.ElapsedMilliseconds;

            if (elapsedTime > 500)
            {
                _loggerService.Warn($"Performance Alert: {typeof(TRequest).Name} took {elapsedTime} ms.");
            }
            else
            {
                _loggerService.Info($"Performance: {typeof(TRequest).Name} took {elapsedTime} ms.");
            }
        }
    }
}

public class PerformanceQueryBehavior<TRequest, TResponse> :
    IQueryPipelineBehavior<TRequest, TResponse>
    where TRequest : IQuery<TResponse>
{
    private readonly LoggerServiceBase _loggerService;

    public PerformanceQueryBehavior(LoggerServiceBase loggerService)
    {
        _loggerService = loggerService;
    }

    public Task<TResponse> Handle(TRequest request, QueryHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        return HandleCore(request, next.Invoke, cancellationToken);
    }

    private async Task<TResponse> HandleCore(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            return await next();
        }
        finally
        {
            stopwatch.Stop();
            var elapsedTime = stopwatch.ElapsedMilliseconds;

            if (elapsedTime > 500)
            {
                _loggerService.Warn($"Performance Alert: {typeof(TRequest).Name} took {elapsedTime} ms.");
            }
            else
            {
                _loggerService.Info($"Performance: {typeof(TRequest).Name} took {elapsedTime} ms.");
            }
        }
    }
}
