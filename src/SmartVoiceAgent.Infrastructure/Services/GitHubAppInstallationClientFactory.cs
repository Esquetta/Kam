using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.GitHub;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class GitHubAppInstallationClientFactory : IGitHubAppClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public GitHubAppInstallationClientFactory(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public IGitHubAppClient Create(GitHubAppOptions options)
    {
        return new GitHubAppInstallationClient(
            _httpClientFactory.CreateClient(nameof(GitHubAppInstallationClient)),
            Options.Create(options));
    }
}
