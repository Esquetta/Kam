using SmartVoiceAgent.Core.Models.GitHub;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IGitHubAppClientFactory
{
    IGitHubAppClient Create(GitHubAppOptions options);
}
