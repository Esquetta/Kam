namespace SmartVoiceAgent.Core.Config
{
    public class HuggingFaceConfig
    {
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = "openai/whisper-large-v3"; // Default model
        public int MaxConcurrentRequests { get; set; } = 5;
        public int MaxAudioSizeBytes { get; set; } = 25 * 1024 * 1024; // 25MB
        public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(5);

        // Alternatif modeller
        public static readonly string[] RecommendedModels = {
        "openai/whisper-large-v3",        // En yüksek kalite
        "openai/whisper-medium",          // Orta kalite, hızlı
        "openai/whisper-base",            // Hızlı, düşük kaynak
        "facebook/wav2vec2-large-960h",   // İngilizce için optimize
        "facebook/wav2vec2-base-960h",    // Hızlı alternatif
        "microsoft/speecht5_asr",         // Microsoft'un modeli
        "jonatasgrosman/wav2vec2-large-xlsr-53-turkish", // Türkçe için
    };

    }
}
