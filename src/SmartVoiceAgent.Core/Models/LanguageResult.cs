namespace SmartVoiceAgent.Core.Entities;
public class LanguageResult
{
    public string Language { get; set; } = "en";
    public float Confidence { get; set; }
    public Dictionary<string, float> AlternativeLanguages { get; set; } = new();
    public bool IsReliable => Confidence > 0.7f;
    public string ErrorMessage { get; set; } = string.Empty;
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);
}