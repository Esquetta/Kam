using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>
/// Decides what of a thread the model sees. The saved thread keeps every message so the UI can show it;
/// a compaction inserts a summary message, and the model only sees that summary and what follows it.
/// </summary>
public static class AgentContextWindow
{
    /// <summary>Text every compaction summary message starts with.</summary>
    public const string SummaryPrefix = "Summary of the earlier conversation";

    /// <summary>Tool results kept in full, counted from the newest, when older ones are shortened.</summary>
    public const int RecentToolResultsKeptInFull = 4;

    private const int ShortenedToolResultCharacters = 400;
    private const int TranscriptToolResultCharacters = 1500;
    private static readonly JsonSerializerOptions CompactJson = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    private const string ShortenedNote =
        "\n[Older tool result shortened to save context. Call the tool again if you need the full output.]";

    /// <summary>
    /// Instructions for the model call that writes a compaction summary.
    /// </summary>
    public const string SummaryInstructions =
        """
        You compress the history of a conversation between a user and Kam, a desktop assistant agent with tools,
        so Kam can continue the work with less context. Keep the user's goals and requests, their decisions and
        preferences, facts learned from tools (paths, names, values, errors), what was done and how it turned out,
        and what is still open. Drop small talk and tool output that no longer matters. Write compact bullet points
        under the headings Goals, Done, Facts and Open, in the language the user writes in. Do not invent anything.
        """;

    /// <summary>
    /// Returns whether a message is a compaction summary.
    /// </summary>
    /// <param name="message">The message.</param>
    public static bool IsSummary(ChatMessage message) =>
        message.Role == ChatRole.System && message.Text.StartsWith(SummaryPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Creates the message a compaction inserts into the thread.
    /// </summary>
    /// <param name="summary">The summary text from the model.</param>
    public static ChatMessage CreateSummaryMessage(string summary) =>
        new(ChatRole.System, $"{SummaryPrefix} (older messages were compacted to save context):\n{summary.Trim()}");

    /// <summary>
    /// Returns the index of the first message the model sees: the one after the newest summary, or 0.
    /// </summary>
    /// <param name="history">The saved thread.</param>
    public static int FindActiveStart(IReadOnlyList<ChatMessage> history)
    {
        for (var index = history.Count - 1; index >= 0; index--)
        {
            if (IsSummary(history[index]))
            {
                return index + 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// Builds the request for the model: the system prompt with the newest summary appended, then the messages after it.
    /// </summary>
    /// <param name="systemPrompt">The agent system prompt.</param>
    /// <param name="history">The saved thread.</param>
    /// <param name="shortenOldToolResults">Whether to shorten all but the newest tool results.</param>
    public static List<ChatMessage> BuildModelMessages(
        string systemPrompt,
        IReadOnlyList<ChatMessage> history,
        bool shortenOldToolResults)
    {
        var start = FindActiveStart(history);
        var system = start > 0 ? $"{systemPrompt}\n\n{history[start - 1].Text}" : systemPrompt;
        var messages = new List<ChatMessage>(history.Count - start + 1) { new(ChatRole.System, system) };

        var keepInFull = shortenOldToolResults
            ? new HashSet<FunctionResultContent>(
                history.Skip(start)
                    .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
                    .Reverse()
                    .Take(RecentToolResultsKeptInFull),
                ReferenceEqualityComparer.Instance)
            : null;

        for (var index = start; index < history.Count; index++)
        {
            var message = history[index];
            if (message.Role == ChatRole.System)
            {
                continue;
            }

            messages.Add(keepInFull is not null && message.Role == ChatRole.Tool
                ? ShortenToolMessage(message, keepInFull)
                : message);
        }

        return messages;
    }

    /// <summary>
    /// Estimates the tokens of messages at about four characters per token.
    /// </summary>
    /// <param name="messages">The messages.</param>
    public static int EstimateTokens(IEnumerable<ChatMessage> messages)
    {
        long characters = 0;
        foreach (var message in messages)
        {
            characters += 16;
            foreach (var content in message.Contents)
            {
                characters += content switch
                {
                    TextContent text => text.Text.Length,
                    FunctionCallContent call => call.Name.Length + SerializeArguments(call.Arguments).Length,
                    FunctionResultContent result => ResultText(result.Result).Length,
                    _ => 0
                };
            }
        }

        return (int)Math.Min(int.MaxValue, characters / 4);
    }

    /// <summary>
    /// Estimates the tokens the tool definitions add to every request.
    /// </summary>
    /// <param name="tools">The tools offered to the model.</param>
    public static int EstimateTokens(IEnumerable<AgentToolDescriptor> tools)
    {
        long characters = 0;
        foreach (var tool in tools)
        {
            characters += tool.Function.Name.Length
                + tool.Function.Description.Length
                + tool.Function.JsonSchema.GetRawText().Length;
        }

        return (int)Math.Min(int.MaxValue, characters / 4);
    }

    /// <summary>
    /// Finds where a compaction should end: the start of the user turn that is kept, counted from the newest.
    /// Returns <c>null</c> when nothing before that point is left to summarize.
    /// </summary>
    /// <param name="history">The saved thread.</param>
    /// <param name="keepUserTurns">How many of the newest user turns stay as they are; 0 summarizes everything.</param>
    public static int? FindCompactionCut(IReadOnlyList<ChatMessage> history, int keepUserTurns)
    {
        var start = FindActiveStart(history);
        if (keepUserTurns <= 0)
        {
            return history.Count > start ? history.Count : null;
        }

        var userTurns = new List<int>();
        for (var index = start; index < history.Count; index++)
        {
            if (history[index].Role == ChatRole.User)
            {
                userTurns.Add(index);
            }
        }

        if (userTurns.Count < keepUserTurns)
        {
            return null;
        }

        var cut = userTurns[^keepUserTurns];
        return cut > start ? cut : null;
    }

    /// <summary>
    /// Writes the part of the thread being compacted as plain text for the summary call, newest content kept
    /// when it is too long. Plain text avoids sending tool messages to a request without tools.
    /// </summary>
    /// <param name="history">The saved thread.</param>
    /// <param name="cut">Index of the first message that is kept.</param>
    /// <param name="maxCharacters">Longest transcript to send.</param>
    public static string RenderTranscript(IReadOnlyList<ChatMessage> history, int cut, int maxCharacters)
    {
        var start = FindActiveStart(history);
        var toolNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var transcript = new StringBuilder();

        for (var index = start; index < cut && index < history.Count; index++)
        {
            var message = history[index];
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent text when !string.IsNullOrWhiteSpace(text.Text):
                        transcript.Append(message.Role == ChatRole.User ? "User: " : "Kam: ")
                            .AppendLine(text.Text.Trim());
                        break;
                    case FunctionCallContent call:
                        toolNames[call.CallId] = call.Name;
                        transcript.Append("Kam called ").Append(call.Name).Append(' ')
                            .AppendLine(SerializeArguments(call.Arguments));
                        break;
                    case FunctionResultContent result:
                        var name = toolNames.GetValueOrDefault(result.CallId, "a tool");
                        transcript.Append("Result of ").Append(name).Append(": ")
                            .AppendLine(Clip(ResultText(result.Result), TranscriptToolResultCharacters));
                        break;
                }
            }
        }

        var body = transcript.ToString();
        if (body.Length > maxCharacters)
        {
            body = "[Earlier part omitted]\n" + body[^maxCharacters..];
        }

        return start > 0
            ? $"{history[start - 1].Text}\n\nConversation since that summary:\n{body}"
            : body;
    }

    private static ChatMessage ShortenToolMessage(ChatMessage message, HashSet<FunctionResultContent> keepInFull)
    {
        var changed = false;
        var contents = new List<AIContent>(message.Contents.Count);
        foreach (var content in message.Contents)
        {
            if (content is FunctionResultContent result && !keepInFull.Contains(result))
            {
                var text = ResultText(result.Result);
                if (text.Length > ShortenedToolResultCharacters + ShortenedNote.Length)
                {
                    contents.Add(new FunctionResultContent(result.CallId, text[..ShortenedToolResultCharacters] + ShortenedNote));
                    changed = true;
                    continue;
                }
            }

            contents.Add(content);
        }

        return changed ? new ChatMessage(ChatRole.Tool, contents) : message;
    }

    private static string Clip(string text, int maxCharacters) =>
        text.Length <= maxCharacters ? text : text[..maxCharacters] + " [...]";

    private static string ResultText(object? result) => result switch
    {
        null => string.Empty,
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
        JsonElement element => element.GetRawText(),
        _ => result.ToString() ?? string.Empty
    };

    private static string SerializeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return "{}";
        }

        try
        {
            return JsonSerializer.Serialize(arguments, CompactJson);
        }
        catch (NotSupportedException)
        {
            return "{}";
        }
    }
}
