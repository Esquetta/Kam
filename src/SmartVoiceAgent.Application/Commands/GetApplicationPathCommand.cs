using MediatR;
using SmartVoiceAgent.Core.Dtos;

namespace SmartVoiceAgent.Application.Commands;
public record GetApplicationPathCommand(string ApplicationName) : IRequest<AgentApplicationResponse>;
