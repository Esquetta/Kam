using Core.CrossCuttingConcerns.Logging.Serilog;
using SmartVoiceAgent.Application.Pipelines.Caching;

namespace SmartVoiceAgent.Application.Behaviors.Logging;

/// <summary>
/// Logs request and response information for commands and queries in the pipeline.
/// </summary>
public class LoggingBehavior<TRequest, TResponse> :
    ICommandPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    private readonly LoggingBehaviorCore<TRequest, TResponse> _core;

    public LoggingBehavior(LoggerServiceBase loggerService)
    {
        _core = new LoggingBehaviorCore<TRequest, TResponse>(loggerService);
    }

    public Task<TResponse> Handle(TRequest request, CommandHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        return _core.HandleCore(request, next.Invoke, cancellationToken);
    }
}

public class LoggingQueryBehavior<TRequest, TResponse> :
    IQueryPipelineBehavior<TRequest, TResponse>
    where TRequest : IQuery<TResponse>
{
    private readonly LoggingBehaviorCore<TRequest, TResponse> _core;

    public LoggingQueryBehavior(LoggerServiceBase loggerService)
    {
        _core = new LoggingBehaviorCore<TRequest, TResponse>(loggerService);
    }

    public Task<TResponse> Handle(TRequest request, QueryHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        return _core.HandleCore(request, next.Invoke, cancellationToken);
    }
}

internal sealed class LoggingBehaviorCore<TRequest, TResponse>
{
    private readonly LoggerServiceBase _loggerService;

    public LoggingBehaviorCore(LoggerServiceBase loggerService)
    {
        _loggerService = loggerService;
    }

    public async Task<TResponse> HandleCore(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken)
    {
        if (request is not ILoggableRequest)
        {
            return await next();
        }

        try
        {
            _loggerService.Info($"Handling {request?.GetType().Name}");

            if (request is ICachableRequest cachableRequest)
            {
                _loggerService.Info($"Cache: {cachableRequest.CacheKey}");
            }

            var response = await next();

            _loggerService.Info($"Handled {request?.GetType().Name}");

            return response;
        }
        catch (Exception ex)
        {
            _loggerService.Error($"Exception in {request?.GetType().Name}: {ex.Message}");
            throw;
        }
    }
}
