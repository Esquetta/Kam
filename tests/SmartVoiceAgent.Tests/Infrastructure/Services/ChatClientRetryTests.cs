using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Infrastructure.Agent.Conf;
using SmartVoiceAgent.Infrastructure.Extensions;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class ChatClientRetryTests
{
    [Theory]
    [InlineData("OpenAI", "/v1")]
    [InlineData("Anthropic", "")]
    public async Task CreateChatClient_WithoutSdkRetries_SendsAFailingRequestOnce(string provider, string path)
    {
        using var server = new UnavailableServer();
        var client = ServiceCollectionExtensions.CreateChatClient(
            new AIServiceConfiguration
            {
                Provider = provider,
                Endpoint = server.Url + path,
                ApiKey = "test-key",
                ModelId = "test-model"
            },
            sdkRetries: false);

        var call = async () => await client.GetResponseAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        await call.Should().ThrowAsync<Exception>();
        server.Requests.Should().Be(1, "the agent loop owns retries, so the SDK must not add hidden ones");
    }

    /// <summary>A local HTTP server that answers every request with 503.</summary>
    private sealed class UnavailableServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private int _requests;

        public UnavailableServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = ServeAsync();
        }

        public string Url { get; }

        public int Requests => Volatile.Read(ref _requests);

        public void Dispose() => _listener.Close();

        private async Task ServeAsync()
        {
            try
            {
                while (true)
                {
                    var context = await _listener.GetContextAsync();
                    Interlocked.Increment(ref _requests);
                    var body = Encoding.UTF8.GetBytes("{\"error\":{\"type\":\"api_error\",\"message\":\"unavailable\"}}");
                    context.Response.StatusCode = 503;
                    context.Response.ContentType = "application/json";
                    await context.Response.OutputStream.WriteAsync(body);
                    context.Response.Close();
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
            {
                // The test is over and the listener is closed.
            }
        }
    }
}
