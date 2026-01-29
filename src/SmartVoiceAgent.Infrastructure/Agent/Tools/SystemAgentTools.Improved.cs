using AgentFrameworkToolkit.Tools;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Commands;
using System.ComponentModel;

namespace SmartVoiceAgent.Infrastructure.Agent.Functions
{
    /// <summary>
    /// Improved System Agent Tools with better AI model compatibility.
    /// Optimized for function calling with various LLM models.
    /// </summary>
    public sealed class SystemAgentToolsImproved
    {
        private readonly IMediator _mediator;
        private readonly ConversationContextManager _contextManager;
        private readonly FileAgentTools _fileTools;
        private readonly ILogger<SystemAgentToolsImproved>? _logger;

        public SystemAgentToolsImproved(
            IMediator mediator,
            ConversationContextManager contextManager,
            FileAgentTools fileTools,
            ILogger<SystemAgentToolsImproved>? logger = null)
        {
            _mediator = mediator;
            _contextManager = contextManager;
            _fileTools = fileTools;
            _logger = logger;
        }

        #region Application Management

        /// <summary>
        /// Opens a desktop application by name. Use this when user wants to open/start/launch an app.
        /// Examples: "open Chrome", "start Spotify", "launch Word"
        /// </summary>
        [AITool("open_application", "Opens a desktop application by name. Call this immediately when user says 'open', 'start', or 'launch' followed by an app name.")]
        public async Task<string> OpenApplication(
            [Description("The exact application name to open. Examples: Chrome, Spotify, Notepad, Word")]
            string applicationName)
        {
            _logger?.LogInformation("SystemAgent: Opening {ApplicationName}", applicationName);

            if (string.IsNullOrWhiteSpace(applicationName))
            {
                return "❌ Error: Application name cannot be empty. Please specify which application to open.";
            }

            if (_contextManager.IsApplicationOpen(applicationName))
            {
                return $