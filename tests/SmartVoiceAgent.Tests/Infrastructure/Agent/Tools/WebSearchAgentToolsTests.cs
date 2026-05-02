using FluentAssertions;
using Moq;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Tools;

public sealed class WebSearchAgentToolsTests
{
    [Fact]
    public async Task SearchWebAsync_DefaultsToSearchOnlyWithoutOpeningBrowser()
    {
        var researchService = new RecordingWebResearchService(
            [
                new WebResearchResult
                {
                    Title = "Kam release notes",
                    Url = "https://example.com/kam",
                    Description = "Production readiness notes."
                }
            ]);
        var tools = new WebSearchAgentTools(Mock.Of<IMediator>(), () => researchService);

        var result = await tools.SearchWebAsync("Kam voice automation", "en", 1);

        result.Should().Contain("Kam release notes");
        result.Should().Contain("https://example.com/kam");
        researchService.SearchCalls.Should().Be(1);
        researchService.SearchAndOpenCalls.Should().Be(0);
    }

    private sealed class RecordingWebResearchService : IWebResearchService
    {
        private readonly List<WebResearchResult> _results;

        public RecordingWebResearchService(List<WebResearchResult> results)
        {
            _results = results;
        }

        public int SearchCalls { get; private set; }

        public int SearchAndOpenCalls { get; private set; }

        public Task<List<WebResearchResult>> SearchAsync(WebResearchRequest request)
        {
            SearchCalls++;
            return Task.FromResult(_results);
        }

        public Task OpenLinksInBrowserAsync(List<WebResearchResult> results)
        {
            return Task.CompletedTask;
        }

        public Task<List<WebResearchResult>> SearchAndOpenAsync(WebResearchRequest request)
        {
            SearchAndOpenCalls++;
            return Task.FromResult(_results);
        }
    }
}
