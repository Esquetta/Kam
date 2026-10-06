using System;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// A tool call waiting for the user's approval, as a notification shows it.
/// </summary>
/// <param name="RequestId">The approval request id.</param>
/// <param name="ThreadTitle">The chat the call belongs to.</param>
/// <param name="ToolName">The tool's display name.</param>
/// <param name="Detail">What the tool does, such as "Runs programs".</param>
/// <param name="IsThreadOnScreen">Whether the chat page shows that thread right now.</param>
/// <param name="Open">Shows the thread with the approval card.</param>
public sealed record ApprovalNotice(
    string RequestId,
    string ThreadTitle,
    string ToolName,
    string Detail,
    bool IsThreadOnScreen,
    Action Open);

/// <summary>
/// Tells the user about approvals they cannot see, for example while Kam is hidden in the tray.
/// </summary>
public interface IApprovalNotifier
{
    /// <summary>
    /// Shows a notification unless the approval card is already in front of the user.
    /// </summary>
    void Show(ApprovalNotice notice);

    /// <summary>
    /// Removes the notification for a request that was answered or ended.
    /// </summary>
    void Dismiss(string requestId);
}
