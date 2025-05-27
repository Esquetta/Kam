using SmartVoiceAgent.Core.Contracts;

namespace SmartVoiceAgent.Application.Commands;
public record SearchWebCommand(string Query) : ICommand;

