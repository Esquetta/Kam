using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.NotificationHandlers;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers
{
    public sealed class CloseApplicationCommandHandler : IRequestHandler<CloseApplicationCommand, CommandResultDTO>
    {
        private readonly IMediator _mediator;
        private readonly IApplicationServiceFactory _factory;
        public CloseApplicationCommandHandler(IMediator mediator, IApplicationServiceFactory factory)
        {
            _mediator = mediator;
            _factory = factory;
        }

        public async Task<CommandResultDTO> Handle(CloseApplicationCommand request, CancellationToken cancellationToken)
        {
            var appService = _factory.Create();
            await appService.CloseApplicationAsync(request.ApplicationName);
            await _mediator.Publish(new ApplicationOpenedNotification(request.ApplicationName), cancellationToken);
            await Task.CompletedTask;
            return new CommandResultDTO(true, $"{request.ApplicationName} application closing...");
        }
    }
}
