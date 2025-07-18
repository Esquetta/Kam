using AutoGen.Core;
using MediatR;
using SmartVoiceAgent.Core.Commands;

namespace SmartVoiceAgent.Application.Agent
{
    public partial class Functions
    {
        private readonly IMediator _mediator;
        public Functions(IMediator mediator)
        {
            _mediator = mediator;
        }
        /// <summary>
        /// Opens a desktop application based on the given name. 
        /// Useful for automating tasks like launching tools or software installed on the system.
        /// </summary>
        /// <param name="applicationName">
        /// The name of the application to open. For example: "chrome", "notepad", "visual studio".
        /// </param>
        /// <returns>
        /// A success message or error information depending on whether the application could be started.
        /// </returns>
        [Function("openApplication")]
        public async Task<CommandResultDTO> HandleAsync(string applicationName)
        {
            return await _mediator.Send(new OpenApplicationCommand(applicationName));
        }
    }
}
