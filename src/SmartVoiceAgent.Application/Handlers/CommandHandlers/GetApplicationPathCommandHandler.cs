using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers
{
    public class GetApplicationPathCommandHandler : IRequestHandler<GetApplicationPathCommand, AgentApplicationResponse>
    {
        private readonly IApplicationServiceFactory _applicationServiceFactory;
        public GetApplicationPathCommandHandler(IApplicationServiceFactory applicationServiceFactory)
        {
            _applicationServiceFactory = applicationServiceFactory;
        }
        public async Task<AgentApplicationResponse> Handle(GetApplicationPathCommand request, CancellationToken cancellationToken)
        {
            var applicationService = _applicationServiceFactory.Create();
            var result = await applicationService.CheckApplicationInstallationAsync(request.ApplicationName);

            return new AgentApplicationResponse
            (
                Success: result.IsInstalled,
                Message: result.IsInstalled ? "Application is installed." : "Application is not installed.",
                ApplicationName: request.ApplicationName,
                ExecutablePath: result.IsInstalled ? result.ExecutablePath : null,
                IsInstalled: result.IsInstalled,
                IsRunning: await applicationService.GetApplicationStatusAsync(request.ApplicationName) == Core.Enums.AppStatus.Running
            );

        }
    }
}
