namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Dispatches queries to their corresponding handlers.
/// </summary>
public interface IQueryBus
{
    /// <summary>
    /// Dispatches the specified query to its handler asynchronously.
    /// </summary>
    /// <typeparam name="TQuery">The type of query.</typeparam>
    /// <typeparam name="TResult">The type of result expected.</typeparam>
    /// <param name="query">The query instance to dispatch.</param>
    /// <returns>The result of the query.</returns>
    Task<TResult> SendAsync<TQuery, TResult>(TQuery query);
}
