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
        /// <summary>
        /// Executes a web search using the provided query, language, and result count.
        /// Returns a plain text summary or result set from the search handler.
        /// </summary>
        [Description("Belirtilen sorgu için web araması yapar ve sonuçları özetlenmiş metin olarak döndürür. Bu araç, sadece arama yapılması gerektiğinde kullanılmalıdır.")]
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
                // Hata durumunda net bir hata mesajı döndür.
                return $"❌ Web araması gerçekleştirilemedi. Hata: {ex.Message}";
            }
        }

        /// <summary>
        /// Retrieves all function tools for this agent as AIFunction instances, 
        /// allowing the Microsoft Agent Framework to register them automatically.
        /// </summary>
        public IEnumerable<AIFunction> GetTools()
        {
            return
            [              
                AIFunctionFactory.Create(SearchWebAsync)
            ];
        }
    }
}
