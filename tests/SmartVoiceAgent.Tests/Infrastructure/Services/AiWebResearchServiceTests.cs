using System.Net;
using Core.CrossCuttingConcerns.Logging.Serilog;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Serilog;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Infrastructure.Services.WebResearch;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class AiWebResearchServiceTests
{
    [Fact]
    public async Task SearchAsync_UsesConfiguredChatClientForResearchPlans()
    {
        var handler = new RecordingWebResearchHandler();
        var chatClient = new QueueingChatClient(
            """
            {"purpose":"smoke","keywords":["Kam voice automation","desktop agent","voice assistant"],"preferredSources":["docs"],"language":"en"}
            """,
            """
            {"selectedIndices":[0],"reasoning":"Relevant smoke result"}
            """);
        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebResearch:SearchApiKey"] = "test-google-key",
                ["WebResearch:SearchEngineId"] = "test-search-engine"
            })
            .Build();
        var service = new AiWebResearchService(httpClient, chatClient, new TestLogger(), configuration);

        var results = await service.SearchAsync(new WebResearchRequest
        {
            Query = "Kam voice automation",
            Language = "en",
            MaxResults = 2
        });

        results.Should().ContainSingle();
        results[0].Title.Should().Be("Kam release notes");
        chatClient.CallCount.Should().Be(2);
        handler.GoogleSearchRequests.Should().Be(2);
    }

    [Fact]
    public async Task SearchAsync_WithSingleResult_UsesDirectSearchWithoutAiPlanning()
    {
        var handler = new RecordingWebResearchHandler();
        var chatClient = new QueueingChatClient();
        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebResearch:SearchApiKey"] = "test-google-key",
                ["WebResearch:SearchEngineId"] = "test-search-engine"
            })
            .Build();
        var service = new AiWebResearchService(httpClient, chatClient, new TestLogger(), configuration);

        var results = await service.SearchAsync(new WebResearchRequest
        {
            Query = "Kam voice automation",
            Language = "en",
            MaxResults = 1
        });

        results.Should().ContainSingle();
        chatClient.CallCount.Should().Be(0);
        handler.GoogleSearchRequests.Should().Be(1);
    }

    [Fact]
    public async Task SearchAsync_WithoutSearchKeys_SearchesDuckDuckGo()
    {
        var handler = new RecordingWebResearchHandler();
        using var httpClient = new HttpClient(handler);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new AiWebResearchService(httpClient, new QueueingChatClient(), new TestLogger(), configuration);

        var results = await service.SearchAsync(new WebResearchRequest { Query = "istanbul hava", Language = "tr", MaxResults = 1 });

        results.Should().ContainSingle();
        results[0].Url.Should().Be("https://www.mgm.gov.tr/tahmin/il-ve-ilceler.aspx?il=Istanbul");
        handler.GoogleSearchRequests.Should().Be(0);
        handler.DuckDuckGoRequests.Should().ContainSingle()
            .Which.Should().Be("POST https://html.duckduckgo.com/html/ q=istanbul+hava&kl=tr-tr");
    }

    [Fact]
    public async Task DuckDuckGoWaitForTurn_SpacesSearchesApart()
    {
        await DuckDuckGoHtmlSearch.WaitForTurnAsync(CancellationToken.None);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await DuckDuckGoHtmlSearch.WaitForTurnAsync(CancellationToken.None);

        stopwatch.Elapsed.Should().BeGreaterThan(DuckDuckGoHtmlSearch.MinimumInterval - TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task SearchAsync_WhenDuckDuckGoShowsItsBotCheck_SearchesBing()
    {
        var handler = new RecordingWebResearchHandler { DuckDuckGoPage = BotCheckPage };
        using var httpClient = new HttpClient(handler);
        var service = new AiWebResearchService(httpClient, new QueueingChatClient(), new TestLogger(), new ConfigurationBuilder().Build());

        var results = await service.SearchAsync(new WebResearchRequest { Query = "kam", Language = "en", MaxResults = 1 });

        results.Should().ContainSingle().Which.Url.Should().Be("https://example.org/bing-result");
        handler.BingRequests.Should().ContainSingle()
            .Which.Should().Be("https://www.bing.com/search?format=rss&q=kam&setlang=en&cc=US");
    }

    [Fact]
    public async Task SearchAsync_WhenDuckDuckGoBlocksAndBingFindsNothing_SaysKeylessSearchIsUnavailable()
    {
        var handler = new RecordingWebResearchHandler { DuckDuckGoPage = BotCheckPage, BingFeed = EmptyBingFeed };
        using var httpClient = new HttpClient(handler);
        var service = new AiWebResearchService(httpClient, new QueueingChatClient(), new TestLogger(), new ConfigurationBuilder().Build());

        var search = () => service.SearchAsync(new WebResearchRequest { Query = "kam", Language = "en", MaxResults = 1 });

        (await search.Should().ThrowAsync<HttpRequestException>())
            .WithMessage("Web search without a key is unavailable*DuckDuckGo is limiting*Google Custom Search key*");
    }

    [Fact]
    public void BingParse_ReadsFeedItems()
    {
        var results = BingRssSearch.Parse(BingFeed, 5);

        results.Should().ContainSingle();
        results[0].Title.Should().Be("Bing & result");
        results[0].Url.Should().Be("https://example.org/bing-result");
        results[0].Description.Should().Be("From the feed.");
        BingRssSearch.Parse("<html>not a feed</html>", 5).Should().BeEmpty();
    }

    private const string BotCheckPage =
        """<html><body><div class="anomaly-modal__title">Unfortunately, bots use DuckDuckGo too.</div></body></html>""";

    private const string BingFeed =
        """
        <?xml version="1.0" encoding="utf-8" ?><rss version="2.0"><channel><title>Bing: kam</title>
        <item><title>Bing &amp; result</title><link>https://example.org/bing-result</link><description>From   the feed.</description></item>
        <item><title>Not web</title><link>ftp://example.org/file</link><description>Skipped</description></item>
        </channel></rss>
        """;

    private const string EmptyBingFeed =
        """<?xml version="1.0" encoding="utf-8" ?><rss version="2.0"><channel><title>Bing: kam</title></channel></rss>""";

    [Fact]
    public void DuckDuckGoParse_ReadsOrganicResultsAndSkipsAds()
    {
        var results = DuckDuckGoHtmlSearch.Parse(DuckDuckGoPage, 5);

        results.Should().HaveCount(2);
        results[0].Title.Should().Be("İstanbul hava durumu & tahmin");
        results[0].Url.Should().Be("https://www.mgm.gov.tr/tahmin/il-ve-ilceler.aspx?il=Istanbul");
        results[0].Description.Should().Be("Saatlik ve 5 günlük tahmin.");
        results[1].Url.Should().Be("https://example.com/direct");
        results[1].Description.Should().BeEmpty();
    }

    [Fact]
    public void DuckDuckGoParse_StopsAtTheLimit()
    {
        DuckDuckGoHtmlSearch.Parse(DuckDuckGoPage, 1).Should().ContainSingle();
        DuckDuckGoHtmlSearch.Parse(string.Empty, 3).Should().BeEmpty();
    }

    private const string DuckDuckGoPage =
        """
        <div class="result results_links result--ad">
          <a rel="nofollow" class="result__a" href="https://duckduckgo.com/y.js?ad_domain=shop.example&amp;u3=x">Sponsored</a>
          <a class="result__snippet" href="https://duckduckgo.com/y.js?u3=x">Buy now</a>
        </div>
        <div class="result results_links">
          <a rel="nofollow" class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fwww.mgm.gov.tr%2Ftahmin%2Fil%2Dve%2Dilceler.aspx%3Fil%3DIstanbul&amp;rut=abc"><b>İstanbul</b> hava durumu &amp; tahmin</a>
          <a class="result__snippet" href="//duckduckgo.com/l/?uddg=x">Saatlik ve   5 günlük <b>tahmin</b>.</a>
        </div>
        <div class="result results_links">
          <a rel="nofollow" class="result__a" href="https://example.com/direct">Direct link</a>
        </div>
        """;

    private sealed class RecordingWebResearchHandler : HttpMessageHandler
    {
        public int GoogleSearchRequests { get; private set; }

        public List<string> DuckDuckGoRequests { get; } = [];

        public string DuckDuckGoPage { get; init; } = AiWebResearchServiceTests.DuckDuckGoPage;

        public List<string> BingRequests { get; } = [];

        public string BingFeed { get; init; } = AiWebResearchServiceTests.BingFeed;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host.Equals("html.duckduckgo.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                var form = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                DuckDuckGoRequests.Add($"{request.Method} {request.RequestUri.AbsoluteUri} {form}");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(DuckDuckGoPage)
                };
            }

            if (request.RequestUri?.Host.Equals("www.bing.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                BingRequests.Add(request.RequestUri.AbsoluteUri);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BingFeed) };
            }

            if (request.Method == HttpMethod.Get
                && request.RequestUri?.Host.Equals("www.googleapis.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                GoogleSearchRequests++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {"items":[{"title":"Kam release notes","link":"https://example.com/kam","snippet":"Production readiness notes."}]}
                        """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent($"Unexpected request: {request.Method} {request.RequestUri}")
            };
        }
    }

    private sealed class QueueingChatClient : IChatClient
    {
        private readonly Queue<string> _responses;

        public QueueingChatClient(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var response = _responses.Count > 0 ? _responses.Dequeue() : string.Empty;
            return Task.FromResult(new ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return EmptyAsync();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> EmptyAsync()
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class TestLogger : LoggerServiceBase
    {
        public TestLogger()
            : base(new LoggerConfiguration().CreateLogger())
        {
        }
    }
}
