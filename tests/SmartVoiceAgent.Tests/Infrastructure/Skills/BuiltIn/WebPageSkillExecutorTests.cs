using System.Net;
using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public class WebPageSkillExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WebFetch_ReturnsBoundedResponseContent()
    {
        using var httpClient = new HttpClient(new StaticHttpMessageHandler(
            "<html><body>Codex fetch response body</body></html>",
            "text/html"));
        var executor = new WebPageSkillExecutor(httpClient);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "web.fetch",
            new
            {
                url = "https://example.com/page",
                maxLength = 25
            }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Status: 200");
        result.Message.Should().Contain("Codex fetch");
        result.Message.Should().Contain("truncated");
    }

    [Fact]
    public async Task ExecuteAsync_WebReadPage_StripsMarkupAndScriptContent()
    {
        using var httpClient = new HttpClient(new StaticHttpMessageHandler(
            """
            <html>
              <head><title>Kam Docs</title><script>ignored()</script></head>
              <body><main><h1>Skill Runtime</h1><p>Readable page text.</p></main></body>
            </html>
            """,
            "text/html"));
        var executor = new WebPageSkillExecutor(httpClient);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "web.read_page",
            new { url = "https://example.com/docs", maxLength = 500 }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Title: Kam Docs");
        result.Message.Should().Contain("Skill Runtime");
        result.Message.Should().Contain("Readable page text");
        result.Message.Should().NotContain("ignored()");
    }

    [Fact]
    public async Task ExecuteAsync_WebFetch_BlocksHostsOutsideRuntimeAllowList()
    {
        using var httpClient = new HttpClient(new StaticHttpMessageHandler("ok", "text/plain"));
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "web.fetch",
            RuntimeOptions = new Dictionary<string, string>
            {
                ["web.allowedHosts"] = "allowed.example"
            }
        });
        var executor = new WebPageSkillExecutor(httpClient, registry);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "web.fetch",
            new { url = "https://blocked.example/page" }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.PermissionDenied);
        result.ErrorCode.Should().Be("web_host_not_allowed");
    }

    [Fact]
    public async Task ExecuteAsync_WebFetch_UsesRuntimePolicyForPrivateNetworkAccess()
    {
        using var httpClient = new HttpClient(new StaticHttpMessageHandler("private ok", "text/plain"));
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "web.fetch",
            RuntimeOptions = new Dictionary<string, string>
            {
                ["web.allowPrivateNetwork"] = "true",
                ["web.allowedHosts"] = "localhost"
            }
        });
        var executor = new WebPageSkillExecutor(httpClient, registry);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "web.fetch",
            new { url = "http://localhost:8080/health" }));

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("private ok");
    }

    [Fact]
    public async Task ExecuteAsync_WebFetch_DoesNotLetPlanArgumentBypassPrivateNetworkPolicy()
    {
        using var httpClient = new HttpClient(new StaticHttpMessageHandler("private ok", "text/plain"));
        var executor = new WebPageSkillExecutor(httpClient);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "web.fetch",
            new
            {
                url = "http://localhost:8080/health",
                allowPrivateNetwork = true
            }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
        result.ErrorCode.Should().Be("invalid_url");
    }

    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _content;
        private readonly string _mediaType;

        public StaticHttpMessageHandler(string content, string mediaType)
        {
            _content = content;
            _mediaType = mediaType;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_content)
            };
            response.Content.Headers.ContentType = new(_mediaType);
            return Task.FromResult(response);
        }
    }
}
