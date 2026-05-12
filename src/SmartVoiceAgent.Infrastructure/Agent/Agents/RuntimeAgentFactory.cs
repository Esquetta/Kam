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
    private readonly IRuntimeAgentRunStore _runStore;
    private readonly IUiLogService? _uiLogService;
    private readonly string _modelId;

    public RuntimeAgentFactory(
        Func<IChatClient> chatClientFactory,
        ILogger<RuntimeAgentFactory> logger,
        IRuntimeAgentRunStore runStore,
        IUiLogService? uiLogService,
        string modelId)
    {
        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _runStore = runStore;
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
        var run = _runStore.Start(normalizedRequest, _modelId);
        _uiLogService?.LogAgentUpdate(
            normalizedRequest.AgentName,
            "Created automatically for this request.",
            false);

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

            _uiLogService?.LogAgentUpdate(normalizedRequest.AgentName, "Completed.", true);
            _runStore.Complete(run.RunId, text);
            return new RuntimeAgentResult(
                normalizedRequest.AgentName,
                normalizedRequest.Role,
                text,
                _modelId,
                run.RunId);
        }
        catch (Exception ex)
        {
            _uiLogService?.LogAgentUpdate(normalizedRequest.AgentName, "Failed.", true);
            _runStore.Fail(run.RunId, ex.Message);
            throw;
        }
    }

    private static RuntimeAgentRequest Normalize(RuntimeAgentRequest request)
    {
        var agentName = NormalizeToken(request.AgentName, "TaskAgent", MaxAgentNameLength);
        var role = NormalizeText(request.Role, "general", MaxRoleLength);
        var userRequest = NormalizeText(request.UserRequest, string.Empty, MaxRequestLength);
        var observations = request.ToolObservations?
            .Where(observation => !string.IsNullOrWhiteSpace(observation.SkillId)
                && !string.IsNullOrWhiteSpace(observation.Summary))
            .Select(observation => observation with
            {
                SkillId = NormalizeObservationSkillId(observation.SkillId),
                Summary = NormalizeText(observation.Summary, string.Empty, 4000)
            })
            .ToArray();

        return new RuntimeAgentRequest(agentName, role, userRequest, observations);
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

    private static string NormalizeObservationSkillId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "tool";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxRoleLength ? trimmed : trimmed[..MaxRoleLength];
    }

    private static string BuildSystemPrompt(RuntimeAgentRequest request)
    {
        var prompt = $"""
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

        if (request.ToolObservations is not { Count: > 0 })
        {
            return prompt;
        }

        var builder = new StringBuilder(prompt)
            .AppendLine()
            .AppendLine("Read-only tool context already gathered for this request:");
        foreach (var observation in request.ToolObservations.Take(5))
        {
            builder
                .AppendLine($"- {observation.SkillId} ({(observation.Success ? "ok" : "failed")}):")
                .AppendLine(observation.Summary);
        }

        return builder.ToString();
    }
}
