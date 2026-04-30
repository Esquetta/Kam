namespace SmartVoiceAgent.Core.Models;
public class WebResearchResult
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime SearchDate { get; set; }
}
public class WebResearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int MaxResults { get; set; } = 5;
    public string Language { get; set; } = "tr";
}
