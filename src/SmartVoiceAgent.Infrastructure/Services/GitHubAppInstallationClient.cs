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
    private const int PullRequestRepositoryLimit = 50;
    private const int PullRequestPageSize = 20;
    private const int WorkflowRunRepositoryLimit = 50;
    private const int WorkflowRunPageSize = 10;
    private const int WorkflowJobPageSize = 100;

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

    public async Task<GitHubPullRequestListResult> ListPullRequestsAsync(
        CancellationToken cancellationToken = default)
    {
        var missingSettings = _options.GetMissingSettings();
        if (missingSettings.Count > 0)
        {
            return GitHubPullRequestListResult.Failed(
                "GitHub App is not configured.",
                missingSettings);
        }

        try
        {
            var jwt = await CreateJsonWebTokenAsync(cancellationToken);
            var token = await CreateInstallationTokenAsync(jwt, cancellationToken);
            var repositories = await GetRepositoriesAsync(token.Token, cancellationToken);
            var pullRequests = new List<GitHubPullRequestSummary>();

            foreach (var repository in repositories.Take(PullRequestRepositoryLimit))
            {
                pullRequests.AddRange(await GetOpenPullRequestsAsync(
                    token.Token,
                    repository.FullName,
                    cancellationToken));
            }

            var repositoryCount = Math.Min(repositories.Count, PullRequestRepositoryLimit);
            var message =
                $"{pullRequests.Count} open {Pluralize(pullRequests.Count, "pull request", "pull requests")} " +
                $"across {repositoryCount} {Pluralize(repositoryCount, "repository", "repositories")}.";
            return GitHubPullRequestListResult.Succeeded(
                message,
                pullRequests
                    .OrderByDescending(pullRequest => pullRequest.UpdatedAt ?? pullRequest.CreatedAt ?? DateTimeOffset.MinValue)
                    .ThenBy(pullRequest => pullRequest.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(pullRequest => pullRequest.Number)
                    .ToArray());
        }
        catch (GitHubAppClientException ex)
        {
            return GitHubPullRequestListResult.Failed(ex.Message);
        }
    }

    public async Task<GitHubWorkflowRunListResult> ListWorkflowRunsAsync(
        CancellationToken cancellationToken = default)
    {
        var missingSettings = _options.GetMissingSettings();
        if (missingSettings.Count > 0)
        {
            return GitHubWorkflowRunListResult.Failed(
                "GitHub App is not configured.",
                missingSettings);
        }

        try
        {
            var jwt = await CreateJsonWebTokenAsync(cancellationToken);
            var token = await CreateInstallationTokenAsync(jwt, cancellationToken);
            var repositories = await GetRepositoriesAsync(token.Token, cancellationToken);
            var workflowRuns = new List<GitHubWorkflowRunSummary>();

            foreach (var repository in repositories.Take(WorkflowRunRepositoryLimit))
            {
                workflowRuns.AddRange(await GetWorkflowRunsAsync(
                    token.Token,
                    repository.FullName,
                    cancellationToken));
            }

            var repositoryCount = Math.Min(repositories.Count, WorkflowRunRepositoryLimit);
            var message =
                $"{workflowRuns.Count} {Pluralize(workflowRuns.Count, "workflow run", "workflow runs")} " +
                $"across {repositoryCount} {Pluralize(repositoryCount, "repository", "repositories")}.";
            return GitHubWorkflowRunListResult.Succeeded(
                message,
                workflowRuns
                    .OrderByDescending(run => run.UpdatedAt ?? run.CreatedAt ?? DateTimeOffset.MinValue)
                    .ThenBy(run => run.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(run => run.Id)
                    .ToArray());
        }
        catch (GitHubAppClientException ex)
        {
            return GitHubWorkflowRunListResult.Failed(ex.Message);
        }
    }

    public async Task<GitHubWorkflowJobListResult> ListWorkflowRunJobsAsync(
        string repositoryFullName,
        long runId,
        CancellationToken cancellationToken = default)
    {
        var missingSettings = _options.GetMissingSettings();
        if (missingSettings.Count > 0)
        {
            return GitHubWorkflowJobListResult.Failed(
                "GitHub App is not configured.",
                missingSettings);
        }

        if (runId <= 0)
        {
            return GitHubWorkflowJobListResult.Failed("GitHub workflow run id must be greater than zero.");
        }

        try
        {
            var jwt = await CreateJsonWebTokenAsync(cancellationToken);
            var token = await CreateInstallationTokenAsync(jwt, cancellationToken);
            var jobs = await GetWorkflowRunJobsAsync(
                token.Token,
                repositoryFullName,
                runId,
                cancellationToken);
            var message =
                $"{jobs.Count} {Pluralize(jobs.Count, "job", "jobs")} for workflow run {runId} in {repositoryFullName}.";
            return GitHubWorkflowJobListResult.Succeeded(
                message,
                repositoryFullName,
                runId,
                jobs
                    .OrderBy(job => job.StartedAt ?? DateTimeOffset.MaxValue)
                    .ThenBy(job => job.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(job => job.Id)
                    .ToArray());
        }
        catch (GitHubAppClientException ex)
        {
            return GitHubWorkflowJobListResult.Failed(ex.Message);
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

    private async Task<IReadOnlyList<GitHubPullRequestSummary>> GetOpenPullRequestsAsync(
        string installationToken,
        string repositoryFullName,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            BuildPullRequestsPath(repositoryFullName),
            installationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubAppClientException(
                BuildPullRequestAccessFailureMessage(response.StatusCode));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var items = await JsonSerializer.DeserializeAsync<IReadOnlyList<GitHubPullRequestItem>>(
            stream,
            JsonOptions,
            cancellationToken);
        return (items ?? [])
            .Where(item => item.Number > 0)
            .Select(item => new GitHubPullRequestSummary(
                repositoryFullName,
                item.Number,
                string.IsNullOrWhiteSpace(item.Title) ? "(untitled)" : item.Title,
                string.IsNullOrWhiteSpace(item.State) ? "unknown" : item.State,
                string.IsNullOrWhiteSpace(item.User?.Login) ? "(unknown)" : item.User.Login,
                item.HtmlUrl ?? string.Empty,
                string.IsNullOrWhiteSpace(item.Head?.Ref) ? "(unknown)" : item.Head.Ref,
                string.IsNullOrWhiteSpace(item.Base?.Ref) ? "(unknown)" : item.Base.Ref,
                item.Draft,
                item.CreatedAt,
                item.UpdatedAt))
            .ToArray();
    }

    private async Task<IReadOnlyList<GitHubWorkflowRunSummary>> GetWorkflowRunsAsync(
        string installationToken,
        string repositoryFullName,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            BuildWorkflowRunsPath(repositoryFullName),
            installationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubAppClientException(
                BuildWorkflowRunAccessFailureMessage(response.StatusCode));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var page = await JsonSerializer.DeserializeAsync<GitHubWorkflowRunPage>(
            stream,
            JsonOptions,
            cancellationToken);
        return (page?.WorkflowRuns ?? [])
            .Where(item => item.Id > 0)
            .Select(item => new GitHubWorkflowRunSummary(
                repositoryFullName,
                item.Id,
                string.IsNullOrWhiteSpace(item.Name) ? "(unnamed)" : item.Name,
                string.IsNullOrWhiteSpace(item.DisplayTitle) ? "(untitled)" : item.DisplayTitle,
                string.IsNullOrWhiteSpace(item.Status) ? "unknown" : item.Status,
                item.Conclusion ?? string.Empty,
                string.IsNullOrWhiteSpace(item.Event) ? "(unknown)" : item.Event,
                string.IsNullOrWhiteSpace(item.HeadBranch) ? "(unknown)" : item.HeadBranch,
                item.HtmlUrl ?? string.Empty,
                item.CreatedAt,
                item.UpdatedAt))
            .ToArray();
    }

    private async Task<IReadOnlyList<GitHubWorkflowJobSummary>> GetWorkflowRunJobsAsync(
        string installationToken,
        string repositoryFullName,
        long runId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            BuildWorkflowRunJobsPath(repositoryFullName, runId),
            installationToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubAppClientException(
                BuildWorkflowRunAccessFailureMessage(response.StatusCode));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var page = await JsonSerializer.DeserializeAsync<GitHubWorkflowJobPage>(
            stream,
            JsonOptions,
            cancellationToken);
        return (page?.Jobs ?? [])
            .Where(item => item.Id > 0)
            .Select(item => new GitHubWorkflowJobSummary(
                repositoryFullName,
                runId,
                item.Id,
                string.IsNullOrWhiteSpace(item.Name) ? "(unnamed)" : item.Name,
                string.IsNullOrWhiteSpace(item.Status) ? "unknown" : item.Status,
                item.Conclusion ?? string.Empty,
                item.HtmlUrl ?? string.Empty,
                item.StartedAt,
                item.CompletedAt))
            .ToArray();
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

    private static string BuildPullRequestAccessFailureMessage(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.Forbidden
            ? "GitHub App pull request list request failed with HTTP 403. Check repository permissions: Metadata and Pull requests."
            : $"GitHub App pull request list request failed with HTTP {(int)statusCode}.";
    }

    private static string BuildWorkflowRunAccessFailureMessage(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.Forbidden
            ? "GitHub App workflow run list request failed with HTTP 403. Check repository permissions: Metadata and Actions."
            : $"GitHub App workflow run list request failed with HTTP {(int)statusCode}.";
    }

    private static string BuildPullRequestsPath(string repositoryFullName)
    {
        var parts = repositoryFullName.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new GitHubAppClientException("GitHub repository full name was invalid.");
        }

        return $"repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/pulls?state=open&per_page={PullRequestPageSize}";
    }

    private static string BuildWorkflowRunsPath(string repositoryFullName)
    {
        var parts = repositoryFullName.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new GitHubAppClientException("GitHub repository full name was invalid.");
        }

        return $"repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/actions/runs?per_page={WorkflowRunPageSize}";
    }

    private static string BuildWorkflowRunJobsPath(string repositoryFullName, long runId)
    {
        var parts = repositoryFullName.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new GitHubAppClientException("GitHub repository full name was invalid.");
        }

        return $"repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/actions/runs/{runId}/jobs?per_page={WorkflowJobPageSize}";
    }

    private static string Pluralize(int count, string singular, string plural)
    {
        return count == 1 ? singular : plural;
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

    private sealed record GitHubPullRequestItem(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("user")] GitHubUser? User,
        [property: JsonPropertyName("head")] GitHubPullRequestRef? Head,
        [property: JsonPropertyName("base")] GitHubPullRequestRef? Base,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt);

    private sealed record GitHubUser(
        [property: JsonPropertyName("login")] string? Login);

    private sealed record GitHubPullRequestRef(
        [property: JsonPropertyName("ref")] string? Ref);

    private sealed record GitHubWorkflowRunPage(
        [property: JsonPropertyName("total_count")] int TotalCount,
        [property: JsonPropertyName("workflow_runs")] IReadOnlyList<GitHubWorkflowRunItem>? WorkflowRuns);

    private sealed record GitHubWorkflowRunItem(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("display_title")] string? DisplayTitle,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("conclusion")] string? Conclusion,
        [property: JsonPropertyName("event")] string? Event,
        [property: JsonPropertyName("head_branch")] string? HeadBranch,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt);

    private sealed record GitHubWorkflowJobPage(
        [property: JsonPropertyName("total_count")] int TotalCount,
        [property: JsonPropertyName("jobs")] IReadOnlyList<GitHubWorkflowJobItem>? Jobs);

    private sealed record GitHubWorkflowJobItem(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("conclusion")] string? Conclusion,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("started_at")] DateTimeOffset? StartedAt,
        [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt);

    private sealed class GitHubAppClientException(string message) : Exception(message);
}
