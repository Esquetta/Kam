using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Updates;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class GitHubApplicationUpdateService : IApplicationUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IApplicationVersionProvider _versionProvider;
    private readonly ApplicationUpdateOptions _options;

    public GitHubApplicationUpdateService(
        HttpClient httpClient,
        IApplicationVersionProvider versionProvider,
        IOptions<ApplicationUpdateOptions>? options = null)
    {
        _httpClient = httpClient;
        _versionProvider = versionProvider;
        _options = options?.Value ?? new ApplicationUpdateOptions();

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Kam-Updater", CurrentVersion));
        }
    }

    public string CurrentVersion => _versionProvider.CurrentVersion;

    public async Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.GetLatestReleaseUri());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return ApplicationUpdateCheckResult.Failed(
                    CurrentVersion,
                    $"No published GitHub release was found for {_options.GetRepositorySlug()}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return ApplicationUpdateCheckResult.Failed(
                    CurrentVersion,
                    $"GitHub release check failed with HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return ApplicationUpdateCheckResult.Failed(
                    CurrentVersion,
                    "GitHub release metadata did not include a version tag.");
            }

            if (release.Prerelease && !_options.IncludePrerelease)
            {
                return ApplicationUpdateCheckResult.Failed(
                    CurrentVersion,
                    "Latest GitHub release is marked as prerelease and prerelease updates are disabled.");
            }

            var latestVersion = NormalizeVersion(release.TagName);
            if (!IsNewerVersion(latestVersion, CurrentVersion))
            {
                return ApplicationUpdateCheckResult.UpToDate(
                    CurrentVersion,
                    latestVersion,
                    release.Name,
                    release.HtmlUrl,
                    release.PublishedAt);
            }

            return ApplicationUpdateCheckResult.UpdateAvailable(
                CurrentVersion,
                latestVersion,
                release.Name,
                release.HtmlUrl,
                release.PublishedAt,
                SelectAsset(release.Assets ?? []));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApplicationUpdateCheckResult.Failed(CurrentVersion, "GitHub release check timed out.");
        }
        catch (HttpRequestException ex)
        {
            return ApplicationUpdateCheckResult.Failed(CurrentVersion, $"GitHub release check failed: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return ApplicationUpdateCheckResult.Failed(CurrentVersion, $"GitHub release metadata could not be parsed: {ex.Message}");
        }
    }

    public async Task<ApplicationUpdateDownloadResult> DownloadLatestAsync(
        CancellationToken cancellationToken = default)
    {
        var update = await CheckForUpdatesAsync(cancellationToken);
        if (!update.Success)
        {
            return ApplicationUpdateDownloadResult.Failed(update.Message);
        }

        if (!update.IsUpdateAvailable)
        {
            return ApplicationUpdateDownloadResult.Failed("No newer Kam release is available to download.");
        }

        if (update.Asset is null || string.IsNullOrWhiteSpace(update.Asset.DownloadUrl))
        {
            return ApplicationUpdateDownloadResult.Failed(
                $"Kam {update.LatestVersion} is available, but no downloadable installer or package asset was found.");
        }

        var tempPath = string.Empty;
        try
        {
            var downloadDirectory = ResolveDownloadDirectory();
            Directory.CreateDirectory(downloadDirectory);
            var filePath = Path.Combine(downloadDirectory, Path.GetFileName(update.Asset.Name));
            tempPath = filePath + ".download";
            using var request = new HttpRequestMessage(HttpMethod.Get, update.Asset.DownloadUrl);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ApplicationUpdateDownloadResult.Failed(
                    $"Update package download failed with HTTP {(int)response.StatusCode}.");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(tempPath))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            File.Move(tempPath, filePath);
            var fileInfo = new FileInfo(filePath);
            return ApplicationUpdateDownloadResult.Succeeded(
                filePath,
                update.LatestVersion,
                fileInfo.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            TryDeleteFile(tempPath);
            return ApplicationUpdateDownloadResult.Failed($"Update package download failed: {ex.Message}");
        }
    }

    private ApplicationUpdateAsset? SelectAsset(IReadOnlyList<GitHubAsset> assets)
    {
        if (!string.IsNullOrWhiteSpace(_options.PreferredAssetName))
        {
            var preferred = assets.FirstOrDefault(asset =>
                asset.Name.Equals(_options.PreferredAssetName, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
            {
                return ToUpdateAsset(preferred);
            }
        }

        var selected = assets
            .OrderBy(asset => GetAssetRank(asset.Name))
            .ThenBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(asset => !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl));

        return selected is null ? null : ToUpdateAsset(selected);
    }

    private static int GetAssetRank(string name)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return extension switch
        {
            ".msi" => 0,
            ".exe" => 1,
            ".zip" => 2,
            _ => 3
        };
    }

    private static ApplicationUpdateAsset ToUpdateAsset(GitHubAsset asset)
    {
        return new ApplicationUpdateAsset(
            asset.Name,
            asset.BrowserDownloadUrl,
            asset.Size,
            asset.ContentType);
    }

    private string ResolveDownloadDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.DownloadDirectory))
        {
            return Path.GetFullPath(_options.DownloadDirectory);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kam",
            "Updates");
    }

    private static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        if (Version.TryParse(NormalizeVersion(latestVersion), out var latest)
            && Version.TryParse(NormalizeVersion(currentVersion), out var current))
        {
            return latest > current;
        }

        return !NormalizeVersion(latestVersion).Equals(
            NormalizeVersion(currentVersion),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string value)
    {
        var version = value.Trim().TrimStart('v', 'V');
        var suffixIndex = version.IndexOfAny(['-', '+']);
        return suffixIndex < 0 ? version : version[..suffixIndex];
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset>? Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("content_type")] string? ContentType);
}
