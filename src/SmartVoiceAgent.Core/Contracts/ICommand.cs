namespace SmartVoiceAgent.Core.Contracts;

/// <summary>
/// Represents a marker interface for a command.
/// </summary>
public interface ICommand { }

/// <summary>
/// Represents a command that returns a result.
/// </summary>
/// <typeparam name="TResult">The type of the result returned by the command.</typeparam>
public interface ICommand<TResult> { }
