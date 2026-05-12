using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Infrastructure.Agent.Agents;

public sealed class RuntimeAgentFactory : IRuntimeAgentFactory
{
    private const int MaxAgentNameLength = 48;
    private const int MaxRoleLength = 120;
    private const int MaxRequestLength = 20000;

    private readonly Func<IChatClient> _chatClientFactory;
    private readonly ILogger<RuntimeAgentFactory> _logger;
    private readonly IUiLogService? _uiLogService;
    private readonly string _modelId;

    public RuntimeAgentFactory(
        Func<IChatClient> chatClientFactory,
        ILogger<RuntimeAgentFactory> logger,
        IUiLogService? uiLogService,
        string modelId)
    {
        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _uiLogService = uiLogService;
        _modelId = modelId;
    }

    public async Task<RuntimeAgentResult> RunAsync(
        RuntimeAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedRequest = Normalize(request);
        _logger.LogInformation(
            "Creating runtime task agent {AgentName} for role {Role}",
            normalizedRequest.AgentName,
            normalizedRequest.Role);
        _uiLogService?.LogAgentUpdate(normalizedRequest.AgentName, "Task agent started", false);

        var messages = new[]
        {
            new ChatMessage(ChatRole.System, BuildSystemPrompt(normalizedRequest)),
            new ChatMessage(ChatRole.User, normalizedRequest.UserRequest)
        };

        try
        {
            var response = await _chatClientFactory()
                .GetResponseAsync(messages, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var text = string.Join(
                    Environment.NewLine,
                    response.Messages
                        .Select(message => message.Text)
                        .Where(message => !string.IsNullOrWhiteSpace(message)))
                .Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                text = "Task agent completed without a text response.";
            }

            _uiLogService?.LogAgentUpdate(normalizedRequest.AgentName, "Task agent completed", true);
            return new RuntimeAgentResult(
                normalizedRequest.AgentName,
                normalizedRequest.Role,
                text,
                _modelId);
        }
        catch
        {
            _uiLogService?.LogAgentUpdate(normalizedRequest.AgentName, "Task agent failed", true);
            throw;
        }
    }

    private static RuntimeAgentRequest Normalize(RuntimeAgentRequest request)
    {
        var agentName = NormalizeToken(request.AgentName, "TaskAgent", MaxAgentNameLength);
        var role = NormalizeText(request.Role, "general", MaxRoleLength);
        var userRequest = NormalizeText(request.UserRequest, string.Empty, MaxRequestLength);

        return new RuntimeAgentRequest(agentName, role, userRequest);
    }

    private static string NormalizeToken(string value, string fallback, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_')
            {
                builder.Append(character);
            }
        }

        if (builder.Length == 0)
        {
            return fallback;
        }

        return builder.Length <= maxLength
            ? builder.ToString()
            : builder.ToString(0, maxLength);
    }

    private static string NormalizeText(string value, string fallback, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string BuildSystemPrompt(RuntimeAgentRequest request)
    {
        return $"""
You are {request.AgentName}, a short-lived Kam task agent created for one request.

Role:
{request.Role}

Operating rules:
- Work only on the current request and do not retain state after completion.
- Use the same language as the user unless the request explicitly asks otherwise.
- Be concise, concrete, and action-oriented.
- You do not have direct desktop, file, shell, browser, network, email, or repository tools in this runtime.
- If the task requires a tool or permissioned action, describe the exact next skill/action Kam should run and any missing setup.
- Do not expose internal class names, stack traces, service names, secrets, or raw configuration values.
""";
    }
}
