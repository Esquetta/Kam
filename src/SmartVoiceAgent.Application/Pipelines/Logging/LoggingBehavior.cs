using Core.CrossCuttingConcerns.Logging;
using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Behaviors.Logging;

/// <summary>
/// Logs request and response information for commands and queries in the pipeline.
/// </summary>
/// <typeparam name="TRequest">The type of request.</typeparam>
/// <typeparam name="TResponse">The type of response.</typeparam>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly LoggerServiceBase _loggerServiceBase;

    public LoggingBehavior(LoggerServiceBase loggerServiceBase)
    {
        _loggerServiceBase = loggerServiceBase;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var logParameters = new List<LogParameter>
        {
            new LogParameter { Type = request.GetType().Name, Value = request }
        };

        var logDetail = new LogDetail
        {
            MethodName = typeof(TRequest).Name,
            Parameters = logParameters
        };

        _loggerServiceBase.Info("Handling request: \n" + JsonSerializer.Serialize(logDetail));

        try
        {
            var response = await next();

            _loggerServiceBase.Info("Handled request successfully: \n" + JsonSerializer.Serialize(logDetail));

            return response;
        }
        catch (Exception ex)
        {
            _loggerServiceBase.Error($"Exception occurred while handling request: {ex.Message}\n" + JsonSerializer.Serialize(logDetail));
            throw;
        }
    }
}
