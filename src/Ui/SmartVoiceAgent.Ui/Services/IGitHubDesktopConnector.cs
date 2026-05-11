using SmartVoiceAgent.Core.Models.GitHub;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Ui.Services;

public interface IGitHubDesktopConnector
{
    Task<GitHubDesktopConnectionResult> ConnectAsync(CancellationToken cancellationToken = default);

    Task<GitHubDesktopConnectionResult> ListRepositoriesAsync(CancellationToken cancellationToken = default);
}

public sealed record GitHubDesktopConnectionResult(
    bool Success,
    string Message,
    IReadOnlyList<GitHubRepositorySummary> Repositories)
{
    public static GitHubDesktopConnectionResult Failed(string message)
    {
        return new GitHubDesktopConnectionResult(false, message, []);
    }

    public static GitHubDesktopConnectionResult Connected(
        string message,
        IReadOnlyList<GitHubRepositorySummary> repositories)
    {
        return new GitHubDesktopConnectionResult(true, message, repositories);
    }
}
