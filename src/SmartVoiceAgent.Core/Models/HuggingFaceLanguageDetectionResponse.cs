namespace SmartVoiceAgent.Core.Models
{
    public class HuggingFaceLanguageDetectionResponse
    {
        public string Label { get; set; } = string.Empty;
        public float Score { get; set; }
    }
}
