using AgentFrameworkToolkit.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using System.ComponentModel;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools
{
    public sealed class WebSearchAgentTools
    {
        private readonly IMediator _mediator;
        private readonly Func<IWebResearchService> _webResearchServiceFactory;

        public WebSearchAgentTools(
            IMediator mediator,
            IServiceProvider serviceProvider)
            : this(
                mediator,
                () => serviceProvider.GetRequiredService<IWebResearchService>())
        {
        }

        public WebSearchAgentTools(
            IMediator mediator,
            Func<IWebResearchService> webResearchServiceFactory)
        {
            _mediator = mediator;
            _webResearchServiceFactory = webResearchServiceFactory;
        }

        [AITool("search_web", "Search given query in web.")]
        public async Task<string> SearchWebAsync(
            [Description("Search query text.")]
            string query,
            [Description("Search language code, for example tr or en.")]
            string lang = "tr",
            [Description("Maximum result count.")]
            int results = 5,
            [Description("If true, open selected results in the browser after searching.")]
            bool openResults = false)
        {
            Console.WriteLine($"WebSearchAgent: Searching web for {query} ({results} results, lang: {lang})");
            try
            {
                if (openResults)
                {
                    var result = await _mediator.SendAsync(new SearchWebCommand(query, lang, results));
                    return result?.ToString() ?? $"No results found for '{query}'.";
                }

                var searchResults = await _webResearchServiceFactory().SearchAsync(new WebResearchRequest
                {
                    Query = query,
                    Language = lang,
                    MaxResults = Math.Clamp(results, 1, 10)
                });

                return FormatSearchResults(query, searchResults);
            }
            catch (Exception ex)
            {
                return $"Web search could not be completed. Error: {ex.Message}";
            }
        }

        private static string FormatSearchResults(
            string query,
            IReadOnlyCollection<WebResearchResult> results)
        {
            if (results.Count == 0)
            {
                return $"No results found for '{query}'.";
            }

            var builder = new StringBuilder();
            builder.AppendLine($"Search results for '{query}' ({results.Count}):");
            foreach (var result in results)
            {
                builder.AppendLine($"- {result.Title}");
                builder.AppendLine($"  URL: {result.Url}");
                if (!string.IsNullOrWhiteSpace(result.Description))
                {
                    builder.AppendLine($"  Summary: {result.Description}");
                }
            }

            return builder.ToString();
        }

        public IEnumerable<AIFunction> GetTools()
        {
            return
            [
                AIFunctionFactory.Create(SearchWebAsync)
            ];
        }
    }
}
