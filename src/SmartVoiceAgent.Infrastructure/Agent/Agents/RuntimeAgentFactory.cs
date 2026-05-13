using System.Text;
using System.Text.Json;
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
    private const int MaxModelToolRequests = 3;

    private readonly Func<IChatClient> _chatClientFactory;
    private readonly ILogger<RuntimeAgentFactory> _logger;
    private readonly IRuntimeAgentRunStore _runStore;
    private readonly IRuntimeAgentReadOnlyToolService? _readOnlyToolService;
    private readonly IUiLogService? _uiLogService;
    private readonly string _modelId;

    public RuntimeAgentFactory(
        Func<IChatClient> chatClientFactory,
        ILogger<RuntimeAgentFactory> logger,
        IRuntimeAgentRunStore runStore,
        IRuntimeAgentReadOnlyToolService? readOnlyToolService,
        IUiLogService? uiLogService,
        string modelId)
    {
        _chatClientFactory = chatClientFactory;
        _logger = logger;
        _runStore = runStore;
        _readOnlyToolService = readOnlyToolService;
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
            false,
            run.RunId);

        var messages = new[]
        {
            new ChatMessage(ChatRole.System, BuildSystemPrompt(normalizedRequest)),
            new ChatMessage(ChatRole.User, normalizedRequest.UserRequest)
        };

        try
        {
            var chatClient = _chatClientFactory();
            var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var text = ExtractText(response);

            var toolRequests = ParseReadOnlyToolRequests(text);
            if (toolRequests.Count > 0 && _readOnlyToolService is not null)
            {
                _uiLogService?.LogAgentUpdate(
                    normalizedRequest.AgentName,
                    $"Requested context: {FormatToolRequestList(toolRequests)}.",
                    false,
                    run.RunId);
                var newObservations = await _readOnlyToolService
                    .ExecuteAsync(toolRequests, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var observation in newObservations)
                {
                    _uiLogService?.LogAgentUpdate(
                        normalizedRequest.AgentName,
                        $"Context {FormatObservationStatus(observation)}: {FormatToolName(observation.SkillId)}.",
                        false,
                        run.RunId);
                }

                normalizedRequest = normalizedRequest with
                {
                    ToolObservations = (normalizedRequest.ToolObservations ?? [])
                        .Concat(newObservations)
                        .ToArray()
                };
                _runStore.RecordToolObservations(run.RunId, normalizedRequest.ToolObservations);
                messages =
                [
                    new ChatMessage(ChatRole.System, BuildSystemPrompt(normalizedRequest)),
                    new ChatMessage(ChatRole.User, normalizedRequest.UserRequest),
                    new ChatMessage(
                        ChatRole.User,
                        "Additional read-only context was gathered. Provide the final answer now; do not request more tools.")
                ];
                response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                text = ExtractText(response);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                text = "Task agent completed without a text response.";
            }

            _uiLogService?.LogAgentUpdate(normalizedRequest.AgentName, "Completed.", true, run.RunId);
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
            _uiLogService?.LogAgentUpdate(normalizedRequest.AgentName, "Failed.", true, run.RunId);
            _runStore.Fail(run.RunId, ex.Message);
            throw;
        }
    }

    private static string ExtractText(ChatResponse response)
    {
        return string.Join(
                Environment.NewLine,
                response.Messages
                    .Select(message => message.Text)
                    .Where(message => !string.IsNullOrWhiteSpace(message)))
            .Trim();
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
        var prompt = $$"""
You are {{request.AgentName}}, a short-lived Kam task agent created for one request.

Role:
{{request.Role}}

Operating rules:
- Work only on the current request and do not retain state after completion.
- Use the same language as the user unless the request explicitly asks otherwise.
- Be concise, concrete, and action-oriented.
- You do not have direct desktop, file, shell, browser, network, email, or repository tools in this runtime.
- You may request one extra round of read-only context by replying with only JSON in this shape:
  {"toolRequests":[{"tool":"file.read_lines","path":"relative/path.cs"},{"tool":"workspace.search_text","query":"term"},{"tool":"git.diff_summary"}]}
- Available read-only tools are exactly: file.read_lines, workspace.search_text, git.diff_summary.
- Do not request shell, write, network, browser, email, GitHub, or mutation tools.
- If the task requires any unavailable or permissioned action, describe the exact next skill/action Kam should run and any missing setup.
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

    private static IReadOnlyList<RuntimeAgentReadOnlyToolRequest> ParseReadOnlyToolRequests(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (!document.RootElement.TryGetProperty("toolRequests", out var requests)
                || requests.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var parsed = new List<RuntimeAgentReadOnlyToolRequest>();
            foreach (var request in requests.EnumerateArray())
            {
                if (request.ValueKind != JsonValueKind.Object
                    || !request.TryGetProperty("tool", out var toolElement))
                {
                    continue;
                }

                var tool = toolElement.GetString();
                if (!IsAllowedReadOnlyTool(tool))
                {
                    continue;
                }

                parsed.Add(new RuntimeAgentReadOnlyToolRequest(
                    tool!,
                    ReadOptionalString(request, "path"),
                    ReadOptionalString(request, "query")));

                if (parsed.Count >= MaxModelToolRequests)
                {
                    break;
                }
            }

            return parsed;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsAllowedReadOnlyTool(string? tool)
    {
        return tool is not null
            && (tool.Equals("file.read_lines", StringComparison.OrdinalIgnoreCase)
                || tool.Equals("workspace.search_text", StringComparison.OrdinalIgnoreCase)
                || tool.Equals("git.diff_summary", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string FormatToolRequestList(IReadOnlyList<RuntimeAgentReadOnlyToolRequest> requests)
    {
        return string.Join(", ", requests.Select(request => FormatToolName(request.Tool)));
    }

    private static string FormatObservationStatus(RuntimeAgentToolObservation observation)
    {
        return observation.Success ? "ready" : "unavailable";
    }

    private static string FormatToolName(string tool)
    {
        return tool.Trim().ToLowerInvariant() switch
        {
            "file.read_lines" => "read file",
            "workspace.search_text" => "search text",
            "git.diff_summary" => "diff summary",
            "workspace.map" => "workspace map",
            _ => "context"
        };
    }
}
