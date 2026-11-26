using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace SmartVoiceAgent.Infrastructure.Agent.Subagents;

/// <summary>
/// The <c>task</c> tool: starts a subagent with its own context and a subset of the tools, waits for it and
/// returns its report. The agent runtime runs it with the call id, so it can tie the subagent's activity to the call.
/// </summary>
public sealed class SubagentTaskFunction : AIFunction
{
    /// <summary>The model-facing tool name.</summary>
    public const string ToolName = "task";

    private static readonly JsonElement Schema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["subagent_type"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(SubagentCatalog.All.Select(definition => (JsonNode)definition.Name).ToArray()),
                ["description"] = "Which kind of subagent does the work."
            },
            ["description"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "A 3-6 word label for the job, shown to the user while it runs."
            },
            ["prompt"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "A complete, self-contained brief: the goal, the files, folders or sites involved, what to report back and when to stop. The subagent does not see this conversation."
            }
        },
        ["required"] = new JsonArray("subagent_type", "description", "prompt")
    });

    private readonly Func<string, AIFunctionArguments, CancellationToken, Task<string>> _run;

    /// <summary>
    /// Creates the tool for one turn.
    /// </summary>
    /// <param name="run">Runs a subagent for a call id and its arguments and returns the report.</param>
    public SubagentTaskFunction(Func<string, AIFunctionArguments, CancellationToken, Task<string>> run)
    {
        _run = run;
    }

    /// <inheritdoc />
    public override string Name => ToolName;

    /// <inheritdoc />
    public override string Description =>
        "Starts a subagent: a separate agent with its own context that works on one job and returns a report. " +
        "Use it for broad searches, research and independent pieces of work, so your own context stays small; " +
        "start several read-only subagents in one response to run them at the same time. " +
        "Do small lookups (one file, one search) yourself. The user does not see the report, so pass on what matters. Types:\n" +
        string.Join("\n", SubagentCatalog.All.Select(definition => $"- {definition.Name}: {definition.Description}"));

    /// <inheritdoc />
    public override JsonElement JsonSchema => Schema;

    /// <summary>Reads a string argument of a call, or null when it is missing.</summary>
    /// <param name="arguments">The call's arguments.</param>
    /// <param name="name">The argument name.</param>
    public static string? ReadString(IDictionary<string, object?>? arguments, string name) =>
        arguments is not null && arguments.TryGetValue(name, out var value)
            ? value switch
            {
                null => null,
                string text => text,
                JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
                JsonElement element => element.GetRawText(),
                _ => value.ToString()
            }
            : null;

    /// <summary>Gets whether a call starts a read-only subagent, which may run next to other read-only calls.</summary>
    /// <param name="arguments">The call's arguments.</param>
    public static bool StartsReadOnlySubagent(IDictionary<string, object?>? arguments) =>
        SubagentCatalog.Find(ReadString(arguments, "subagent_type"))?.ReadOnly == true;

    /// <summary>Runs the subagent for <paramref name="callId"/>.</summary>
    /// <param name="callId">The model's call id.</param>
    /// <param name="arguments">The call's arguments.</param>
    /// <param name="cancellationToken">Stops the subagent.</param>
    public Task<string> RunAsync(string callId, AIFunctionArguments arguments, CancellationToken cancellationToken) =>
        _run(callId, arguments, cancellationToken);

    /// <inheritdoc />
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
        await _run(Guid.NewGuid().ToString("N"), arguments, cancellationToken);
}
