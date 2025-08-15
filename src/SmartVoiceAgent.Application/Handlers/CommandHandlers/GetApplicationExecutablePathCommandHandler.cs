using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers
{
    public class GetApplicationExecutablePathCommandHandler:IRequestHandler<GetApplicationExecutablePathCommand, CommandResultDTO>
    {
        private readonly IMediator _mediator;
        private readonly IApplicationServiceFactory _factory;
        public GetApplicationExecutablePathCommandHandler(IMediator mediator, IApplicationServiceFactory factory)
        {
            _mediator = mediator;
            _factory = factory;
        }

        public async Task<CommandResultDTO> Handle(GetApplicationExecutablePathCommand request, CancellationToken cancellationToken)
        {
            var appService = _factory.Create();
            //await appService(request.ApplicationName);
            await Task.CompletedTask;
            return new CommandResultDTO(true, $"{request.ApplicationName} execute path found...");
        }
    }
}
