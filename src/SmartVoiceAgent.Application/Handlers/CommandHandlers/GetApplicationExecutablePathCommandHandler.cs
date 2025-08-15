using MediatR;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Application.Handlers.CommandHandlers
{
    public class GetApplicationExecutablePathCommandHandler : IRequestHandler<GetApplicationExecutablePathCommand, string>
    {
        private readonly IMediator _mediator;
        private readonly IApplicationServiceFactory _factory;
        public GetApplicationExecutablePathCommandHandler(IMediator mediator, IApplicationServiceFactory factory)
        {
            _mediator = mediator;
            _factory = factory;
        }

        public async Task<string> Handle(GetApplicationExecutablePathCommand request, CancellationToken cancellationToken)
        {
            var appService = _factory.Create();
            var result = await appService.GetApplicationExecutablePathAsync(request.ApplicationName);
            await Task.CompletedTask;
            return result;
        }
    }
}
