using System.Text;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Planning;

public sealed class ModelSkillPlannerService : ISkillPlannerService
{
    private readonly IChatClient _chatClient;
    private readonly ISkillRegistry _skillRegistry;

    public ModelSkillPlannerService(IChatClient chatClient, ISkillRegistry skillRegistry)
    {
        _chatClient = chatClient;
        _skillRegistry = skillRegistry;
    }

    public async Task<SkillPlanParseResult> CreatePlanAsync(
        string userRequest,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userRequest))
        {
            return SkillPlanParseResult.Failure("User request is required.");
        }

        var messages = new[]
        {
            new ChatMessage(ChatRole.System, BuildSystemPrompt()),
            new ChatMessage(ChatRole.User, userRequest.Trim())
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        var responseText = string.Join(
            Environment.NewLine,
            response.Messages
                .Select(message => message.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        return SkillPlanParser.ParseStrictJsonObject(responseText);
    }

    private string BuildSystemPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are Kam's model planner.");
        builder.AppendLine("Return exactly one JSON object and nothing else.");
        builder.AppendLine("Do not call tools, do not use markdown, and do not explain outside JSON.");
        builder.AppendLine("JSON schema:");
        builder.AppendLine("""{"skillId":"skill.id","arguments":{},"confidence":0.0,"requiresConfirmation":false,"reasoning":"short reason"}""");
        builder.AppendLine("Choose one skillId from the available skills below.");
        builder.AppendLine("Available skills:");

        foreach (var skill in _skillRegistry.GetAll().OrderBy(skill => skill.Id, StringComparer.OrdinalIgnoreCase))
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
}
