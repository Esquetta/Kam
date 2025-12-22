using AgentFrameworkToolkit.Tools;
using MediatR;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Application.Commands;
using System.ComponentModel;

namespace SmartVoiceAgent.Infrastructure.Agent.Tools
{
    public sealed class WebSearchAgentTools
    {
        private readonly IMediator _mediator;
        public WebSearchAgentTools(IMediator mediator)
        {
            _mediator = mediator;
        }
        [AITool("search_web","Search given query in web.")]
        public async Task<string> SearchWebAsync(
            [Description("Web'de aranacak sorgu metni (örn: 'İstanbul hava durumu')")]
        string query,
            [Description("Arama dil kodu (örn: 'tr', 'en')")]
        string lang = "tr",
            [Description("Döndürülecek maksimum sonuç sayısı")]
        int results = 5)
        {
            Console.WriteLine($"WebSearchAgent: Searching web for {query} ({results} results, lang: {lang})");
            try
            {
                
                var result = await _mediator.Send(new SearchWebCommand(query, lang, results));

                
                return result?.ToString() ?? $"'{query}' araması sonuçsuz kaldı.";
            }
            catch (Exception ex)
            {
                return $"❌ Web araması gerçekleştirilemedi. Hata: {ex.Message}";
            }
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
