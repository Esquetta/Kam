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
    public async Task ListPullRequestsAsync_WhenConfigured_UsesInstallationTokenAndReturnsOpenPullRequests()
    {
        var privateKeyPath = WritePrivateKey();
        var seenInstallationToken = string.Empty;
        var client = CreateClient(new StaticHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/app/installations/98765/access_tokens")
            {
                return JsonResponse("""
                    {
                      "token": "installation-token",
                      "expires_at": "2026-05-10T12:00:00Z"
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/installation/repositories")
            {
                return JsonResponse("""
                    {
                      "total_count": 1,
                      "repositories": [
                        {
                          "full_name": "Esquetta/Kam",
                          "private": true,
                          "html_url": "https://github.com/Esquetta/Kam",
                          "clone_url": "https://github.com/Esquetta/Kam.git",
                          "default_branch": "master"
                        }
                      ]
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/repos/Esquetta/Kam/pulls")
            {
                seenInstallationToken = request.Headers.Authorization?.Parameter ?? string.Empty;
                request.Headers.Authorization?.Scheme.Should().Be("Bearer");
                request.RequestUri.Query.Should().Contain("state=open");
                request.RequestUri.Query.Should().Contain("per_page=20");

                return JsonResponse("""
                    [
                      {
                        "number": 42,
                        "title": "Fix CI",
                        "state": "open",
                        "html_url": "https://github.com/Esquetta/Kam/pull/42",
                        "draft": false,
                        "user": { "login": "alice" },
                        "head": { "ref": "feature/fix-ci" },
                        "base": { "ref": "master" },
                        "created_at": "2026-05-10T12:00:00Z",
                        "updated_at": "2026-05-11T12:00:00Z"
                      }
                    ]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }), new GitHubAppOptions
        {
            AppId = "12345",
            InstallationId = "98765",
            PrivateKeyPath = privateKeyPath
        });

        var result = await client.ListPullRequestsAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("1 open pull request");
        result.PullRequests.Should().ContainSingle();
        result.PullRequests[0].RepositoryFullName.Should().Be("Esquetta/Kam");
        result.PullRequests[0].Number.Should().Be(42);
        result.PullRequests[0].Title.Should().Be("Fix CI");
        result.PullRequests[0].AuthorLogin.Should().Be("alice");
        result.PullRequests[0].HeadRefName.Should().Be("feature/fix-ci");
        result.PullRequests[0].BaseRefName.Should().Be("master");
        seenInstallationToken.Should().Be("installation-token");
        result.Message.Should().NotContain("installation-token");
        result.Message.Should().NotContain(privateKeyPath);
    }

    [Fact]
    public async Task ListWorkflowRunsAsync_WhenConfigured_UsesInstallationTokenAndReturnsWorkflowRuns()
    {
        var privateKeyPath = WritePrivateKey();
        var seenInstallationToken = string.Empty;
        var client = CreateClient(new StaticHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/app/installations/98765/access_tokens")
            {
                return JsonResponse("""
                    {
                      "token": "installation-token",
                      "expires_at": "2026-05-10T12:00:00Z"
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/installation/repositories")
            {
                return JsonResponse("""
                    {
                      "total_count": 1,
                      "repositories": [
                        {
                          "full_name": "Esquetta/Kam",
                          "private": true,
                          "html_url": "https://github.com/Esquetta/Kam",
                          "clone_url": "https://github.com/Esquetta/Kam.git",
                          "default_branch": "master"
                        }
                      ]
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/repos/Esquetta/Kam/actions/runs")
            {
                seenInstallationToken = request.Headers.Authorization?.Parameter ?? string.Empty;
                request.Headers.Authorization?.Scheme.Should().Be("Bearer");
                request.RequestUri.Query.Should().Contain("per_page=10");

                return JsonResponse("""
                    {
                      "total_count": 1,
                      "workflow_runs": [
                        {
                          "id": 1001,
                          "name": ".NET CI",
                          "display_title": "Add GitHub App PR slash command",
                          "status": "completed",
                          "conclusion": "success",
                          "event": "push",
                          "head_branch": "master",
                          "html_url": "https://github.com/Esquetta/Kam/actions/runs/1001",
                          "created_at": "2026-05-11T10:00:00Z",
                          "updated_at": "2026-05-11T10:05:00Z"
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

        var result = await client.ListWorkflowRunsAsync();

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("1 workflow run");
        result.WorkflowRuns.Should().ContainSingle();
        result.WorkflowRuns[0].RepositoryFullName.Should().Be("Esquetta/Kam");
        result.WorkflowRuns[0].Id.Should().Be(1001);
        result.WorkflowRuns[0].Name.Should().Be(".NET CI");
        result.WorkflowRuns[0].DisplayTitle.Should().Be("Add GitHub App PR slash command");
        result.WorkflowRuns[0].Status.Should().Be("completed");
        result.WorkflowRuns[0].Conclusion.Should().Be("success");
        result.WorkflowRuns[0].Event.Should().Be("push");
        result.WorkflowRuns[0].HeadBranch.Should().Be("master");
        seenInstallationToken.Should().Be("installation-token");
        result.Message.Should().NotContain("installation-token");
        result.Message.Should().NotContain(privateKeyPath);
    }

    [Fact]
    public async Task ListWorkflowRunJobsAsync_WhenConfigured_UsesInstallationTokenAndReturnsJobs()
    {
        var privateKeyPath = WritePrivateKey();
        var seenInstallationToken = string.Empty;
        var client = CreateClient(new StaticHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/app/installations/98765/access_tokens")
            {
                return JsonResponse("""
                    {
                      "token": "installation-token",
                      "expires_at": "2026-05-10T12:00:00Z"
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/repos/Esquetta/Kam/actions/runs/1001/jobs")
            {
                seenInstallationToken = request.Headers.Authorization?.Parameter ?? string.Empty;
                request.Headers.Authorization?.Scheme.Should().Be("Bearer");
                request.RequestUri.Query.Should().Contain("per_page=100");

                return JsonResponse("""
                    {
                      "total_count": 2,
                      "jobs": [
                        {
                          "id": 2001,
                          "name": "build",
                          "status": "completed",
                          "conclusion": "success",
                          "html_url": "https://github.com/Esquetta/Kam/actions/runs/1001/job/2001",
                          "started_at": "2026-05-11T10:00:00Z",
                          "completed_at": "2026-05-11T10:05:00Z"
                        },
                        {
                          "id": 2002,
                          "name": "security-scan",
                          "status": "completed",
                          "conclusion": "failure",
                          "html_url": "https://github.com/Esquetta/Kam/actions/runs/1001/job/2002",
                          "started_at": "2026-05-11T10:00:00Z",
                          "completed_at": "2026-05-11T10:04:00Z"
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

        var result = await client.ListWorkflowRunJobsAsync("Esquetta/Kam", 1001);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("2 jobs");
        result.RepositoryFullName.Should().Be("Esquetta/Kam");
        result.RunId.Should().Be(1001);
        result.Jobs.Should().HaveCount(2);
        result.Jobs[0].Name.Should().Be("build");
        result.Jobs[0].Status.Should().Be("completed");
        result.Jobs[0].Conclusion.Should().Be("success");
        result.Jobs[1].Name.Should().Be("security-scan");
        result.Jobs[1].Conclusion.Should().Be("failure");
        seenInstallationToken.Should().Be("installation-token");
        result.Message.Should().NotContain("installation-token");
        result.Message.Should().NotContain(privateKeyPath);
    }

    [Fact]
    public async Task GetStatusAsync_WhenInstallationHasNoRepositories_ReturnsRepoAccessGuidance()
    {
        var privateKeyPath = WritePrivateKey();
        var client = CreateClient(new StaticHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/app")
            {
                return JsonResponse("""
                    {
                      "name": "Kam Coding Agent",
                      "slug": "kam-coding-agent"
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/app/installations/98765/access_tokens")
            {
                return JsonResponse("""
                    {
                      "token": "installation-token",
                      "expires_at": "2026-05-10T12:00:00Z"
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/installation/repositories")
            {
                return JsonResponse("""
                    {
                      "total_count": 0,
                      "repositories": []
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

        var status = await client.GetStatusAsync();

        status.IsConfigured.Should().BeTrue();
        status.IsConnected.Should().BeFalse();
        status.AppName.Should().Be("Kam Coding Agent");
        status.RepositoryCount.Should().Be(0);
        status.Message.Should().Contain("No repositories");
        status.Message.Should().Contain("install");
        status.Message.Should().Contain("repository access");
        status.Message.Should().NotContain(privateKeyPath);
    }

    [Fact]
    public async Task GetStatusAsync_WhenRepositoryAccessIsForbidden_ReturnsPermissionGuidance()
    {
        var privateKeyPath = WritePrivateKey();
        var client = CreateClient(new StaticHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/app")
            {
                return JsonResponse("""
                    {
                      "name": "Kam Coding Agent",
                      "slug": "kam-coding-agent"
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/app/installations/98765/access_tokens")
            {
                return JsonResponse("""
                    {
                      "token": "installation-token",
                      "expires_at": "2026-05-10T12:00:00Z"
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath == "/installation/repositories")
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }), new GitHubAppOptions
        {
            AppId = "12345",
            InstallationId = "98765",
            PrivateKeyPath = privateKeyPath
        });

        var status = await client.GetStatusAsync();

        status.IsConfigured.Should().BeTrue();
        status.IsConnected.Should().BeFalse();
        status.Message.Should().Contain("repository permissions");
        status.Message.Should().Contain("Contents");
        status.Message.Should().Contain("Dependabot alerts");
        status.Message.Should().NotContain(privateKeyPath);
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
