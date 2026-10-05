using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Decides whether a tool call may run, using the approval mode and the user's saved rules.
/// </summary>
public interface IToolPermissionService
{
    /// <summary>Gets or sets the active approval mode.</summary>
    ApprovalMode Mode { get; set; }

    /// <summary>
    /// Checks a tool call.
    /// </summary>
    /// <param name="tool">The tool being called.</param>
    /// <param name="argumentsJson">The call arguments as JSON.</param>
    ToolPermissionDecision Evaluate(AgentToolDescriptor tool, string argumentsJson);

    /// <summary>
    /// Saves an "always allow" rule for a tool so future calls run without asking.
    /// </summary>
    /// <param name="toolName">The model-facing tool name.</param>
    void AlwaysAllow(string toolName);

    /// <summary>Gets the tool names the user has always allowed.</summary>
    IReadOnlyCollection<string> AllowedTools { get; }

    /// <summary>Removes a saved "always allow" rule.</summary>
    /// <param name="toolName">The model-facing tool name.</param>
    void Revoke(string toolName);
}
