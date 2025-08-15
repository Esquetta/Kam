using MediatR;
using SmartVoiceAgent.Core.Dtos;

namespace SmartVoiceAgent.Application.Commands;

public record CheckApplicationCommand(string ApplicationName) : IRequest<AgentApplicationResponse>;
