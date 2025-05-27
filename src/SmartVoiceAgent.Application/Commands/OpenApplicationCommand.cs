using SmartVoiceAgent.Core.Contracts;

namespace SmartVoiceAgent.Core.Commands;

/// <summary>
/// Command to open an application.
/// </summary>
/// <param name="ApplicationName">The name of the application to open.</param>
public record OpenApplicationCommand(string ApplicationName) : ICommand<CommandResultDTO>;


