using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.GitHub;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class GitHubAppInstallationClient : IGitHubAppClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly GitHubAppOptions _options;

    public GitHubAppInstallationClient(
        HttpClient httpClient,
        IOptions<GitHubAppOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Kam-GitHubApp", "1.0"));
        }
    }

    public async Task<GitHubAppConnectionStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var missingSettings = _options.GetMissingSettings();
        if (missingSettings.Count > 0)
        {
            return GitHubAppConnectionStatus.NotConfigured(
                "GitHub App is not configured.",
                missingSettings);
        }

        try
        {
            var jwt = await CreateJsonWebTokenAsync(cancellationToken);
            var app = await GetAuthenticatedAppAsync(jwt, cancellationToken);
            var token = await CreateInstallationTokenAsync(jwt, cancellationToken);
            var repositoryCount = await GetRepositoryCountAsync(token.Token, cancellationToken);

            if (repositoryCount <= 0)
            {
                return GitHubAppConnectionStatus.RepositoryAccessMissing(
                    _options.AppId.Trim(),
                    _options.InstallationId.Trim(),
                    _options.GetApiBaseUri().ToString().TrimEnd('/'),
                    app.Name,
                    app.Slug);
            }

            return GitHubAppConnectionStatus.Connected(
                _options.AppId.Trim(),
                _options.InstallationId.Trim(),
                _options.GetApiBaseUri().ToString().TrimEnd('/'),
                app.Name,
                app.Slug,
                repositoryCount);
        }
        catch (GitHubAppClientException ex)
        {
            return GitHubAppConnectionStatus.Failed(
                ex.Message,
                _options.AppId.Trim(),
                _options.InstallationId.Trim(),
                _options.GetApiBaseUri().ToString().TrimEnd('/'));
        }
    }

    public async Task<GitHubRepositoryListResult> ListRepositoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var missingSettings = _options.GetMissingSettings();
        if (missingSettings.Count > 0)
        {
            return GitHubRepositoryListResult.Failed(
                "GitHub App is not configured.",
                missingSettings);
        }

        try
        {
            var jwt = await CreateJsonWebTokenAsync(cancellationToken);
            var token = await CreateInstallationTokenAsync(jwt, cancellationToken);
            var repositories = await GetRepositoriesAsync(token.Token, cancellationToken);
            return GitHubRepositoryListResult.Succeeded(
                $"{repositories.Count} repositories accessible.",
                repositories);
        }
        catch (GitHubAppClientException ex)
        {
            return GitHubRepositoryListResult.Failed(ex.Message);
        }
    }

    private async Task<GitHubAppInfo> GetAuthenticatedAppAsync(
        string jwt,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, "app", jwt);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubAppClientException(
                $"GitHub App status request failed with HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<GitHubAppInfo>(stream, JsonOptions, cancellationToken)
            ?? new GitHubAppInfo(null, null);
    }

    private async Task<GitHubInstallationToken> CreateInstallationTokenAsync(
        string jwt,
        CancellationToken cancellationToken)
    {
        var path = $"app/installations/{Uri.EscapeDataString(_options.InstallationId.Trim())}/access_tokens";
        using var request = CreateRequest(HttpMethod.Post, path, jwt);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubAppClientException(
                BuildInstallationTokenFailureMessage(response.StatusCode));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var token = await JsonSerializer.DeserializeAsync<GitHubInstallationToken>(
            stream,
            JsonOptions,
            cancellationToken);
        if (token is null || string.IsNullOrWhiteSpace(token.Token))
        {
            throw new GitHubAppClientException("GitHub App installation token response was empty.");
        }

        return token;
    }

    private async Task<int> GetRepositoryCountAsync(
        string installationToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            "installation/repositories?per_page=1",
            installationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubAppClientException(
                BuildRepositoryAccessFailureMessage(response.StatusCode));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var page = await JsonSerializer.DeserializeAsync<GitHubRepositoryPage>(
            stream,
            JsonOptions,
            cancellationToken);
        return page?.TotalCount ?? 0;
    }

    private async Task<IReadOnlyList<GitHubRepositorySummary>> GetRepositoriesAsync(
        string installationToken,
        CancellationToken cancellationToken)
    {
        var repositories = new List<GitHubRepositorySummary>();
        for (var pageNumber = 1; pageNumber <= 10; pageNumber++)
        {
            using var request = CreateRequest(
                HttpMethod.Get,
                $"installation/repositories?per_page=100&page={pageNumber}",
                installationToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new GitHubAppClientException(
                    BuildRepositoryAccessFailureMessage(response.StatusCode));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<GitHubRepositoryPage>(
                stream,
                JsonOptions,
                cancellationToken);
            var items = page?.Repositories ?? [];
            repositories.AddRange(items
                .Where(item => !string.IsNullOrWhiteSpace(item.FullName))
                .Select(item => new GitHubRepositorySummary(
                    item.FullName,
                    item.Private,
                    string.IsNullOrWhiteSpace(item.DefaultBranch) ? "(unknown)" : item.DefaultBranch,
                    item.HtmlUrl ?? string.Empty,
                    item.CloneUrl ?? string.Empty)));

            if (items.Count < 100)
            {
                break;
            }
        }

        return repositories;
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string pathAndQuery,
        string bearerToken)
    {
        var request = new HttpRequestMessage(method, new Uri(_options.GetApiBaseUri(), pathAndQuery));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (!string.IsNullOrWhiteSpace(_options.ApiVersion))
        {
            request.Headers.Add("X-GitHub-Api-Version", _options.ApiVersion.Trim());
        }

        return request;
    }

    private async Task<string> CreateJsonWebTokenAsync(CancellationToken cancellationToken)
    {
        string privateKey;
        try
        {
            privateKey = await File.ReadAllTextAsync(_options.PrivateKeyPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new GitHubAppClientException("GitHub App private key could not be loaded.");
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKey);

            var now = DateTimeOffset.UtcNow;
            var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = "RS256",
                typ = "JWT"
            }));
            var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
            {
                ["iat"] = now.AddSeconds(-60).ToUnixTimeSeconds(),
                ["exp"] = now.AddMinutes(9).ToUnixTimeSeconds(),
                ["iss"] = _options.AppId.Trim()
            }));
            var unsignedToken = $"{header}.{payload}";
            var signature = rsa.SignData(
                Encoding.UTF8.GetBytes(unsignedToken),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            return $"{unsignedToken}.{Base64UrlEncode(signature)}";
        }
        catch (CryptographicException)
        {
            throw new GitHubAppClientException("GitHub App private key could not be parsed.");
        }
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }

    private static string BuildInstallationTokenFailureMessage(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.NotFound
            ? "GitHub App installation token request failed with HTTP 404. Verify GitHubApp:InstallationId and confirm the app is installed on the target account."
            : $"GitHub App installation token request failed with HTTP {(int)statusCode}.";
    }

    private static string BuildRepositoryAccessFailureMessage(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.Forbidden
            ? "GitHub App repository request failed with HTTP 403. Check repository permissions: Metadata, Contents, Pull requests, Issues, Actions, Checks, Commit statuses, and Dependabot alerts."
            : $"GitHub App repository request failed with HTTP {(int)statusCode}.";
    }

    private sealed record GitHubInstallationToken(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt);

    private sealed record GitHubAppInfo(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("slug")] string? Slug);

    private sealed record GitHubRepositoryPage(
        [property: JsonPropertyName("total_count")] int TotalCount,
        [property: JsonPropertyName("repositories")] IReadOnlyList<GitHubRepositoryItem>? Repositories);

    private sealed record GitHubRepositoryItem(
        [property: JsonPropertyName("full_name")] string FullName,
        [property: JsonPropertyName("private")] bool Private,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("clone_url")] string? CloneUrl,
        [property: JsonPropertyName("default_branch")] string? DefaultBranch);

    private sealed class GitHubAppClientException(string message) : Exception(message);
}
