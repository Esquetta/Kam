namespace SmartVoiceAgent.Core.Models.CodingAgent;

public sealed class CodingAgentOptions
{
    public const string SectionName = "CodingAgent";

    public bool IsEnabled { get; set; }

    public string WorkspaceRoot { get; set; } = string.Empty;

    public string ApprovalMode { get; set; } = "workspace-write";

    public bool RequireShellAllowList { get; set; } = true;

    public string? GetWorkspaceRootOrDefault()
    {
        var root = string.IsNullOrWhiteSpace(WorkspaceRoot)
            ? Environment.CurrentDirectory
            : WorkspaceRoot.Trim();

        try
        {
            return Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
