using SmartVoiceAgent.Core.Contracts;

namespace SmartVoiceAgent.Application.Commands;

public record SendMessageCommand(string Recipient, string Message) : ICommand<CommandResultDTO>;
