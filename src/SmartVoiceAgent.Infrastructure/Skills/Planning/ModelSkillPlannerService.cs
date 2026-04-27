using System.Text;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Planning;

public sealed class ModelSkillPlannerService : ISkillPlannerService
{
    private readonly IChatClient _chatClient;
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISkillPlannerTraceStore? _traceStore;

    public ModelSkillPlannerService(
        IChatClient chatClient,
        ISkillRegistry skillRegistry,
        ISkillPlannerTraceStore? traceStore = null)
    {
        _chatClient = chatClient;
        _skillRegistry = skillRegistry;
        _traceStore = traceStore;
    }

    public async Task<SkillPlanParseResult> CreatePlanAsync(
        string userRequest,
        CancellationToken cancellationToken = default)
    {
        var request = userRequest?.Trim() ?? string.Empty;
        var stopwatch = Stopwatch.StartNew();
        var skills = _skillRegistry
            .GetAll()
            .OrderBy(skill => skill.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var systemPrompt = BuildSystemPrompt(skills);

        if (string.IsNullOrWhiteSpace(request))
        {
            var failure = SkillPlanParseResult.Failure("User request is required.");
            RecordTrace(request, systemPrompt, string.Empty, failure, stopwatch, skills.Length);
            return failure;
        }

        var messages = new[]
        {
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, request)
        };

        string responseText;
        try
        {
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            responseText = string.Join(
                Environment.NewLine,
                response.Messages
                    .Select(message => message.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
        }
        catch (Exception ex)
        {
            var failure = SkillPlanParseResult.Failure(ex.Message);
            RecordTrace(request, systemPrompt, string.Empty, failure, stopwatch, skills.Length);
            throw;
        }

        var result = SkillPlanParser.ParseStrictJsonObject(responseText);
        RecordTrace(request, systemPrompt, responseText, result, stopwatch, skills.Length);

        return result;
    }

    private string BuildSystemPrompt(IReadOnlyCollection<KamSkillManifest> skills)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are Kam's model planner.");
        builder.AppendLine("Return exactly one JSON object and nothing else.");
        builder.AppendLine("Do not call tools, do not use markdown, and do not explain outside JSON.");
        builder.AppendLine("JSON schema:");
        builder.AppendLine("""{"skillId":"skill.id","arguments":{},"confidence":0.0,"requiresConfirmation":false,"reasoning":"short reason"}""");
        builder.AppendLine("Choose one skillId from the available skills below.");
        builder.AppendLine("Available skills:");

        foreach (var skill in skills)
        {
            builder.Append("- ");
            builder.Append(skill.Id);
            if (!string.IsNullOrWhiteSpace(skill.Description))
            {
                builder.Append(": ");
                builder.Append(skill.Description);
            }

            if (skill.Arguments.Count > 0)
            {
                builder.Append(" Args: ");
                builder.Append(string.Join(
                    ", ",
                    skill.Arguments.Select(argument =>
                        $"{argument.Name}:{argument.Type.ToString().ToLowerInvariant()}{(argument.Required ? ":required" : ":optional")}")));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private void RecordTrace(
        string userRequest,
        string systemPrompt,
        string rawResponse,
        SkillPlanParseResult result,
        Stopwatch stopwatch,
        int availableSkillCount)
    {
        if (_traceStore is null)
        {
            return;
        }

        stopwatch.Stop();
        _traceStore.Record(new SkillPlannerTraceEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            UserRequest = userRequest,
            SystemPrompt = systemPrompt,
            RawResponse = rawResponse,
            IsValid = result.IsValid,
            SkillId = result.Plan?.SkillId ?? string.Empty,
            Confidence = result.Plan?.Confidence ?? 0,
            RequiresConfirmation = result.Plan?.RequiresConfirmation ?? false,
            Reasoning = result.Plan?.Reasoning ?? string.Empty,
            ErrorMessage = result.ErrorMessage,
            DurationMilliseconds = stopwatch.ElapsedMilliseconds,
            AvailableSkillCount = availableSkillCount
        });
    }
}
