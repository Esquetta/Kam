using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class GitHubAppInstallationClientTests : IDisposable
{
    private readonly string _settingsDirectory;

    public GitHubAppInstallationClientTests()
    {
        _settingsDirectory = Path.Combine(Path.GetTempPath(), "kam-github-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_settingsDirectory);
    }

    [Fact]
    public async Task GetStatusAsync_WhenGitHubAppOptionsAreMissing_ReturnsSetupGuidanceWithoutSecrets()
    {
        var client = CreateClient(new StaticHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP requests should be sent.")), new GitHubAppOptions());

        var status = await client.GetStatusAsync();

        status.IsConfigured.Should().BeFalse();
        status.IsConnected.Should().BeFalse();
        status.Message.Should().Contain("not configured");
        status.MissingSettings.Should().Contain([
            "GitHubApp:AppId",
            "GitHubApp:InstallationId",
            "GitHubApp:PrivateKeyPath"
        ]);
        status.Message.ToLowerInvariant().Should().NotContain("private-key");
    }

    [Fact]
    public async Task ListRepositoriesAsync_WhenConfigured_UsesInstallationTokenAndReturnsRepositorySummaries()
    {
        var privateKeyPath = WritePrivateKey();
        var seenJwt = string.Empty;
        var seenInstallationToken = string.Empty;
        var client = CreateClient(new StaticHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/app/installations/98765/access_tokens")
            {
                seenJwt = request.Headers.Authorization?.Parameter ?? string.Empty;
                request.Method.Should().Be(HttpMethod.Post);
                request.Headers.Authorization?.Scheme.Should().Be("Bearer");
                request.Headers.Accept.ToString().Should().Contain("application/vnd.github+json");

                return JsonResponse("""
                    {
                      "token": "installation-token",
                      "expires_at": "2026-05-10T12:00:00Z"
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/installation/repositories")
            {
                seenInstallationToken = request.Headers.Authorization?.Parameter ?? string.Empty;
                request.Headers.Authorization?.Scheme.Should().Be("Bearer");
                request.RequestUri.Query.Should().Contain("per_page=100");

                return JsonResponse("""
                    {
                      "total_count": 2,
                      "repositories": [
                        {
                          "full_name": "Esquetta/Kam",
                          "private": true,
                          "html_url": "https://github.com/Esquetta/Kam",
                          "clone_url": "https://github.com/Esquetta/Kam.git",
                          "default_branch": "master"
                        },
                        {
                          "full_name": "Esquetta/PublicTool",
                          "private": false,
                          "html_url": "https://github.com/Esquetta/PublicTool",
                          "clone_url": "https://github.com/Esquetta/PublicTool.git",
                          "default_branch": "main"
                        }
                      ]
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }), new GitHubAppOptions
        {
            AppId = "12345",
            InstallationId = "98765",
            PrivateKeyPath = privateKeyPath
        });

        var result = await client.ListRepositoriesAsync();

        result.Success.Should().BeTrue();
        result.Repositories.Should().HaveCount(2);
        result.Repositories[0].FullName.Should().Be("Esquetta/Kam");
        result.Repositories[0].IsPrivate.Should().BeTrue();
        result.Repositories[1].DefaultBranch.Should().Be("main");
        seenInstallationToken.Should().Be("installation-token");
        var jwtParts = seenJwt.Split('.');
        jwtParts.Should().HaveCount(3);
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            Encoding.UTF8.GetString(Base64UrlDecode(jwtParts[1])));
        payload.Should().NotBeNull();
        payload!["iss"].GetString().Should().Be("12345");
    }

    [Fact]
    public async Task GetStatusAsync_WhenPrivateKeyCannotBeLoaded_DoesNotExposeConfiguredPrivateKeyPath()
    {
        var missingPath = Path.Combine(_settingsDirectory, "private-key.pem");
        var client = CreateClient(new StaticHttpMessageHandler(_ =>
            throw new InvalidOperationException("No HTTP requests should be sent.")), new GitHubAppOptions
        {
            AppId = "12345",
            InstallationId = "98765",
            PrivateKeyPath = missingPath
        });

        var status = await client.GetStatusAsync();

        status.IsConfigured.Should().BeTrue();
        status.IsConnected.Should().BeFalse();
        status.Message.Should().Contain("private key");
        status.Message.Should().NotContain(missingPath);
    }

    private GitHubAppInstallationClient CreateClient(
        HttpMessageHandler handler,
        GitHubAppOptions options)
    {
        return new GitHubAppInstallationClient(
            new HttpClient(handler),
            Options.Create(options));
    }

    private string WritePrivateKey()
    {
        using var rsa = RSA.Create(2048);
        var path = Path.Combine(_settingsDirectory, "github-app-key.pem");
        File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem());
        return path;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
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
        try
        {
            if (Directory.Exists(_settingsDirectory))
            {
                Directory.Delete(_settingsDirectory, recursive: true);
            }
        }
        catch
        {
            // Cleanup must not hide assertion failures.
        }
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
