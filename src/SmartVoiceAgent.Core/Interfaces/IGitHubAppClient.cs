using SmartVoiceAgent.Core.Models.GitHub;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IGitHubAppClient
{
    Task<GitHubAppConnectionStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<GitHubRepositoryListResult> ListRepositoriesAsync(
        CancellationToken cancellationToken = default);
}
