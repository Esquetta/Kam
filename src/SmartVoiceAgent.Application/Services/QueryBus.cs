using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Services;

/// <summary>
/// Dispatches queries to their corresponding handlers.
/// </summary>
public sealed class QueryBus(IServiceProvider serviceProvider) : IQueryBus
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public async Task<TResult> SendAsync<TQuery, TResult>(TQuery query)
    {
        var handler = _serviceProvider.GetService(typeof(IQueryHandler<TQuery, TResult>))
            as IQueryHandler<TQuery, TResult>;

        if (handler is null)
            throw new InvalidOperationException($"No handler registered for query type {typeof(TQuery).Name}");

        return await handler.HandleAsync(query);
    }
}
