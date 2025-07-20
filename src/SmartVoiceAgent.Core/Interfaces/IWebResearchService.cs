using SmartVoiceAgent.Core.Models;

namespace SmartVoiceAgent.Core.Interfaces;
public interface IWebResearchService
{
    Task<List<WebResearchResult>> SearchAsync(WebResearchRequest request);
    Task OpenLinksInBrowserAsync(List<WebResearchResult> results);
    Task<List<WebResearchResult>> SearchAndOpenAsync(WebResearchRequest request);
}