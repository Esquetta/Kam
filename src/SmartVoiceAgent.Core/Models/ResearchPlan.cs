namespace SmartVoiceAgent.Core.Models;
/// <summary>
/// AI araştırma planı modeli
/// </summary>
public class ResearchPlan
{
    public string Purpose { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public List<string> PreferredSources { get; set; } = new();
    public string Language { get; set; } = "tr";
}