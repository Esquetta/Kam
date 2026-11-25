using Microsoft.Extensions.AI;

namespace SmartVoiceAgent.Core.Models.Agents;

/// <summary>
/// A tool the agent can call, with the metadata the runtime needs to gate it.
/// </summary>
/// <param name="Function">The callable function exposed to the model.</param>
/// <param name="Risk">What the tool can change.</param>
/// <param name="Source">Where the tool comes from, such as "builtin" or "mcp:github".</param>
/// <param name="DisplayName">Human-readable name shown in approval cards and the activity log.</param>
/// <param name="PrepareArguments">
/// Fills in the defaults the tool would apply, before the call is shown for approval, so the user approves the
/// arguments that will actually run. Null leaves the model's arguments as they are.
/// </param>
public sealed record AgentToolDescriptor(
    AIFunction Function,
    ToolRisk Risk,
    string Source,
    string DisplayName,
    Func<IDictionary<string, object?>, IDictionary<string, object?>>? PrepareArguments = null)
{
    /// <summary>Gets the model-facing tool name.</summary>
    public string Name => Function.Name;
}
