namespace SmartVoiceAgent.Core.Contracts
{
    /// <summary>
    /// Marker interface for commands.
    /// </summary>
    public interface ICommand { }

    /// <summary>
    /// Marker interface for queries with a response type.
    /// </summary>
    /// <typeparam name="TResult"></typeparam>
    public interface IQuery<TResult> { }
}
