namespace SmartVoiceAgent.Core.Models;
public class WebResearchResult
{
    public string Title { get; set; }
    public string Url { get; set; }
    public string Description { get; set; }
    public DateTime SearchDate { get; set; }
}
public class WebResearchRequest
{
    public string Query { get; set; }
    public int MaxResults { get; set; } = 5;
    public string Language { get; set; } = "tr";
}