using MediatR;
using SmartVoiceAgent.Core.Entities;

namespace SmartVoiceAgent.Application.Commands;
public record ListInstalledApplicationsCommand(bool IncludeSystemApps = false) : IRequest<CommandResult>;

