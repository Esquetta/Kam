namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Represents a handler for a query of type <typeparamref name="TQuery"/> that returns a result of type <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TQuery">The type of query.</typeparam>
/// <typeparam name="TResult">The type of result returned.</typeparam>
public interface IQueryHandler<TQuery, TResult>
{
    /// <summary>
    /// Handles the specified query asynchronously and returns a result.
    /// </summary>
    /// <param name="query">The query instance to handle.</param>
    /// <returns>The result of the query.</returns>
    Task<TResult> HandleAsync(TQuery query);
}
