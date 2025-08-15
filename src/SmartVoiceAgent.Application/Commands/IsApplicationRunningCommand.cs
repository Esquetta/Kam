using MediatR;
using SmartVoiceAgent.Core.Dtos;

namespace SmartVoiceAgent.Application.Commands;

public record IsApplicationRunningCommand(string ApplicationName) : IRequest<AgentApplicationResponse>;
