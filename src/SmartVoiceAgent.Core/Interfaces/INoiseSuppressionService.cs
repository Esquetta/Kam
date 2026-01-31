namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service for audio noise suppression and enhancement.
/// </summary>
public interface INoiseSuppressionService
{
    /// <summary>
    /// Processes audio data to remove background noise.
    /// </summary>
    /// <param name="audioData">Raw PCM audio data (16-bit, mono)</param>
    /// <param name="sampleRate">Sample rate of the audio</param>
    /// <returns>Denoised audio data</returns>
    byte[] SuppressNoise(byte[] audioData, int sampleRate = 16000);

    /// <summary>
    /// Processes audio with advanced options.
    /// </summary>
    /// <param name="audioData">Raw PCM audio data</param>
    /// <param name="options">Noise suppression options</param>
    /// <returns>Denoised audio data</returns>
    byte[] SuppressNoise(byte[] audioData, NoiseSuppressionOptions options);

    /// <summary>
    /// Estimates the noise level in the audio.
    /// </summary>
    /// <param name="audioData">Raw PCM audio data</param>
    /// <param name="sampleRate">Sample rate</param>
    /// <returns>Noise level (0.0 to 1.0)</returns>
    float EstimateNoiseLevel(byte[] audioData, int sampleRate = 16000);

    /// <summary>
    /// Applies automatic gain control to normalize audio levels.
    /// </summary>
    /// <param name="audioData">Raw PCM audio data</param>
    /// <param name="targetLevel">Target RMS level (0.0 to 1.0)</param>
    /// <returns>Normalized audio data</returns>
    byte[] ApplyAGC(byte[] audioData, float targetLevel = 0.3f);

    /// <summary>
    /// Removes echo from audio (for speaker feedback scenarios).
    /// </summary>
    /// <param name="audioData">Raw PCM audio data</param>
    /// <param name="referenceData">Reference audio (echo source)</param>
    /// <param name="sampleRate">Sample rate</param>
    /// <returns>Echo-canceled audio</returns>
    byte[] RemoveEcho(byte[] audioData, byte[] referenceData, int sampleRate = 16000);

    /// <summary>
    /// Gets whether the service is initialized and ready.
    /// </summary>
    bool IsInitialized { get; }
}

/// <summary>
/// Options for noise suppression processing.
/// </summary>
public class NoiseSuppressionOptions
{
    /// <summary>
    /// Sample rate of the audio. Default: 16000
    /// </summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>
    /// Noise suppression strength (0.0 to 1.0). Higher values remove more noise but may affect speech quality.
    /// Default: 0.7
    /// </summary>
    public float SuppressionStrength { get; set; } = 0.7f;

    /// <summary>
    /// Whether to apply automatic gain control. Default: true
    /// </summary>
    public bool ApplyAGC { get; set; } = true;

    /// <summary>
    /// Target level for AGC (0.0 to 1.0). Default: 0.3
    /// </summary>
    public float TargetLevel { get; set; } = 0.3f;

    /// <summary>
    /// Whether to apply high-pass filter to remove low-frequency noise. Default: true
    /// </summary>
    public bool ApplyHighPassFilter { get; set; } = true;

    /// <summary>
    /// High-pass filter cutoff frequency in Hz. Default: 80
    /// </summary>
    public float HighPassCutoff { get; set; } = 80f;

    /// <summary>
    /// Whether to apply voice activity detection to remove silent parts. Default: false
    /// </summary>
    public bool RemoveSilence { get; set; } = false;

    /// <summary>
    /// Silence threshold (0.0 to 1.0). Default: 0.01
    /// </summary>
    public float SilenceThreshold { get; set; } = 0.01f;
}

/// <summary>
/// Result of noise analysis.
/// </summary>
public class NoiseAnalysisResult
{
    /// <summary>
    /// Overall noise level (0.0 to 1.0).
    /// </summary>
    public float NoiseLevel { get; set; }

    /// <summary>
    /// Signal-to-noise ratio in dB.
    /// </summary>
    public float SNR { get; set; }

    /// <summary>
    /// Estimated background noise RMS level.
    /// </summary>
    public float BackgroundNoiseRMS { get; set; }

    /// <summary>
    /// Estimated speech RMS level.
    /// </summary>
    public float SpeechRMS { get; set; }

    /// <summary>
    /// Whether the audio quality is acceptable for speech recognition.
    /// </summary>
    public bool IsQualityAcceptable => SNR > 10f;
}
