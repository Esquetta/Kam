using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Updates;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class GitHubApplicationUpdateServiceTests : IDisposable
{
    private readonly string _downloadDirectory;

    public GitHubApplicationUpdateServiceTests()
    {
        _downloadDirectory = Path.Combine(Path.GetTempPath(), "kam-update-tests", Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenReleaseIsNewer_ReturnsInstallerAsset()
    {
        var service = CreateService(new StaticHttpMessageHandler(request =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/repos/Esquetta/Kam/releases/latest");
            return JsonResponse("""
                {
                  "tag_name": "v1.2.0",
                  "name": "Kam 1.2.0",
                  "html_url": "https://github.com/Esquetta/Kam/releases/tag/v1.2.0",
                  "published_at": "2026-05-09T12:00:00Z",
                  "prerelease": false,
                  "assets": [
                    {
                      "name": "Kam-1.2.0-x64.zip",
                      "browser_download_url": "https://downloads.example/kam.zip",
                      "size": 50,
                      "content_type": "application/zip"
                    },
                    {
                      "name": "Kam-1.2.0-x64.msi",
                      "browser_download_url": "https://downloads.example/kam.msi",
                      "size": 100,
                      "content_type": "application/octet-stream"
                    }
                  ]
                }
                """);
        }));

        var result = await service.CheckForUpdatesAsync();

        result.Success.Should().BeTrue();
        result.IsUpdateAvailable.Should().BeTrue();
        result.CurrentVersion.Should().Be("1.0.0");
        result.LatestVersion.Should().Be("1.2.0");
        result.Asset.Should().NotBeNull();
        result.Asset!.Name.Should().Be("Kam-1.2.0-x64.msi");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenReleaseIsNotNewer_ReturnsUpToDate()
    {
        var service = CreateService(new StaticHttpMessageHandler(_ => JsonResponse("""
            {
              "tag_name": "v1.0.0",
              "name": "Kam 1.0.0",
              "html_url": "https://github.com/Esquetta/Kam/releases/tag/v1.0.0",
              "published_at": "2026-05-09T12:00:00Z",
              "prerelease": false,
              "assets": []
            }
            """)));

        var result = await service.CheckForUpdatesAsync();

        result.Success.Should().BeTrue();
        result.IsUpdateAvailable.Should().BeFalse();
        result.Message.Should().Contain("up to date");
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenReleaseDoesNotExist_ReturnsGracefulFailure()
    {
        var service = CreateService(new StaticHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await service.CheckForUpdatesAsync();

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("No published GitHub release");
    }

    [Fact]
    public async Task DownloadLatestAsync_StoresInstallerPayloadInConfiguredDirectory()
    {
        var payload = Encoding.UTF8.GetBytes("installer-bytes");
        var service = CreateService(new StaticHttpMessageHandler(request =>
        {
            if (request.RequestUri!.Host == "api.github.com")
            {
                return JsonResponse("""
                    {
                      "tag_name": "v1.2.0",
                      "name": "Kam 1.2.0",
                      "html_url": "https://github.com/Esquetta/Kam/releases/tag/v1.2.0",
                      "published_at": "2026-05-09T12:00:00Z",
                      "prerelease": false,
                      "assets": [
                        {
                          "name": "Kam-1.2.0-x64.msi",
                          "browser_download_url": "https://downloads.example/Kam-1.2.0-x64.msi",
                          "size": 15,
                          "content_type": "application/octet-stream"
                        }
                      ]
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        }));

        var result = await service.DownloadLatestAsync();

        result.Success.Should().BeTrue();
        result.Version.Should().Be("1.2.0");
        result.FilePath.Should().NotBeNull();
        File.ReadAllBytes(result.FilePath!).Should().Equal(payload);
    }

    private GitHubApplicationUpdateService CreateService(HttpMessageHandler handler)
    {
        return new GitHubApplicationUpdateService(
            new HttpClient(handler),
            new FakeApplicationVersionProvider("1.0.0"),
            Options.Create(new ApplicationUpdateOptions
            {
                Owner = "Esquetta",
                Repository = "Kam",
                DownloadDirectory = _downloadDirectory
            }));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_downloadDirectory))
        {
            Directory.Delete(_downloadDirectory, recursive: true);
        }
    }

    private sealed class FakeApplicationVersionProvider(string currentVersion) : IApplicationVersionProvider
    {
        public string CurrentVersion { get; } = currentVersion;
    }

    private sealed class StaticHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
