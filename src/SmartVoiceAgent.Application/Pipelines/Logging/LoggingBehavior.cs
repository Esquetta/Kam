using Core.CrossCuttingConcerns.Logging;
using Core.CrossCuttingConcerns.Logging.Serilog;
using MediatR;
using SmartVoiceAgent.Application.Pipelines.Caching;
using System.Text.Json;

namespace SmartVoiceAgent.Application.Behaviors.Logging
{
    /// <summary>
    /// Logs request and response information for commands and queries in the pipeline.
    /// </summary>
    /// <typeparam name="TRequest">The type of request.</typeparam>
    /// <typeparam name="TResponse">The type of response.</typeparam>
    public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly LoggerServiceBase _loggerService;

        public LoggingBehavior(LoggerServiceBase loggerService)
        {
            _loggerService = loggerService;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            var logParameters = new List<LogParameter>
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
                _loggerService.Info($"Handling Request: \n{JsonSerializer.Serialize(logDetail)}");

                
                if (request is ICachableRequest cachableRequest)
                {
                    _loggerService.Info($"Cache Info -> CacheKey: {cachableRequest.CacheKey}, BypassCache: {cachableRequest.BypassCache}, GroupKey: {cachableRequest.CacheGroupKey}");
                }

                var response = await next();

                var responseLog = new LogDetail
                {
                    MethodName = next.Method.Name,
                    Parameters = new List<LogParameter>
                    {
                        new LogParameter { Type = typeof(TResponse).Name, Value = response }
                    }
                };

                _loggerService.Info($"Request Handled: \n{JsonSerializer.Serialize(responseLog)}");

                return response;
            }
            catch (Exception ex)
            {
                _loggerService.Error($"Exception occurred: {ex.Message}\n{JsonSerializer.Serialize(logDetail)}");
                throw;
            }
        }
    }
}
