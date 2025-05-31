using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Concrete implementation for dispatching queries.
/// </summary>
public class QueryBus : IQueryBus
{
    private readonly IServiceProvider _serviceProvider;

    public QueryBus(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<TResult> SendAsync<TQuery, TResult>(TQuery query)
    {
        var handler = _serviceProvider.GetService(typeof(IQueryHandler<TQuery, TResult>)) as IQueryHandler<TQuery, TResult>;

        if (handler is null)
            throw new InvalidOperationException($"No query handler registered for {typeof(TQuery).Name}");

        return await handler.HandleAsync(query);
    }
}
