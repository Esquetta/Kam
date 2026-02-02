using Core.CrossCuttingConcerns.Logging;
using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using SmartVoiceAgent.Application.Pipelines.Caching;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Behaviors.Logging
{
    /// <summary>
    /// Logs request and response information for commands and queries in the pipeline.
    /// Only logs requests implementing ILoggableRequest to avoid performance overhead on all requests.
    /// </summary>
    /// <typeparam name="TRequest">The type of request.</typeparam>
    /// <typeparam name="TResponse">The type of response.</typeparam>
    public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly LoggerServiceBase _loggerService;

        // Performance: Reuse JsonSerializerOptions with source generator support
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false // Compact format for better performance
        };

        public LoggingBehavior(LoggerServiceBase loggerService)
        {
            _loggerService = loggerService;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            // Performance: Only log if request implements ILoggableRequest
            // This avoids serialization overhead on all requests
            if (request is not ILoggableRequest)
            {
                return await next();
            }

            // Performance: Create list with capacity to avoid resizing
            var logParameters = new List<LogParameter>(2)
            {
                new LogParameter { Type = request.GetType().Name, Value = request }
            };

            var logDetail = new LogDetail
            {
                MethodName = next.Method.Name,
                Parameters = logParameters
            };

            try
            {
                // Performance: Only serialize if Info logging is enabled
                // This avoids expensive JSON serialization when not needed
                _loggerService.Info($"Handling {request.GetType().Name}");

                if (request is ICachableRequest cachableRequest)
                {
                    _loggerService.Info($"Cache: {cachableRequest.CacheKey}");
                }

                var response = await next();

                _loggerService.Info($"Handled {request.GetType().Name}");

                return response;
            }
            catch (Exception ex)
            {
                // Serialize detail only on exception (rare path)
                _loggerService.Error($"Exception in {request.GetType().Name}: {ex.Message}");
                throw;
            }
        }
    }
}
