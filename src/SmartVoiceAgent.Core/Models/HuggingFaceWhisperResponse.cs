using System.Text.Json.Serialization;

namespace SmartVoiceAgent.Core.Models
{
    public class HuggingFaceWhisperResponse
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    public class HuggingFaceGenericResponse
    {
        [JsonPropertyName("transcription_text")]
        public string TranscriptionText { get; set; } = string.Empty;

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    public class HuggingFaceErrorResponse
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;

        [JsonPropertyName("estimated_time")]
        public int? EstimatedTime { get; set; }
    }
}
