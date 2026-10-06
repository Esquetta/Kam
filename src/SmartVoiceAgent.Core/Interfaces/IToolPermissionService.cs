using SmartVoiceAgent.Core.Models.Agents;

namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Decides whether a tool call may run, using the approval mode and the user's saved rules.
/// </summary>
/// <remarks>
/// A rule is a tool name, optionally with a pattern for its main argument: <c>files_read</c>,
/// <c>mcp__github__*</c>, <c>shell_run(git status:*)</c> or <c>files_write(C:/work/*)</c>.
/// Deny rules win over allow rules, and both win over the mode.
/// </remarks>
public interface IToolPermissionService
{
    /// <summary>Gets or sets the active approval mode.</summary>
    ApprovalMode Mode { get; set; }

    /// <summary>Gets the saved allow rules.</summary>
    IReadOnlyCollection<string> AllowRules { get; }

    /// <summary>Gets the saved deny rules.</summary>
    IReadOnlyCollection<string> DenyRules { get; }

    /// <summary>
    /// Checks a tool call.
    /// </summary>
    /// <param name="tool">The tool being called.</param>
    /// <param name="argumentsJson">The call arguments as JSON.</param>
    ToolPermissionDecision Evaluate(AgentToolDescriptor tool, string argumentsJson);

    /// <summary>
    /// Returns the allow rule that "Always allow" saves for this call: the tool name, or for shell commands
    /// the command and its subcommand, such as <c>shell_run(git status:*)</c>.
    /// </summary>
    /// <param name="tool">The tool being called.</param>
    /// <param name="argumentsJson">The call arguments as JSON.</param>
    string SuggestAllowRule(AgentToolDescriptor tool, string argumentsJson);

    /// <summary>
    /// Saves a rule.
    /// </summary>
    /// <param name="rule">The rule text.</param>
    /// <param name="allow"><c>true</c> for an allow rule, <c>false</c> for a deny rule.</param>
    void AddRule(string rule, bool allow);

    /// <summary>
    /// Removes a saved rule from both lists.
    /// </summary>
    /// <param name="rule">The rule text.</param>
    void RemoveRule(string rule);
}
