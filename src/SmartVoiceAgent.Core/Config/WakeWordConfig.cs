namespace SmartVoiceAgent.Core.Config;

/// <summary>
/// Configuration for wake word (hotword) detection.
/// </summary>
public class WakeWordConfig
{
    /// <summary>
    /// The wake word phrase to detect. Default: "Hey Kam"
    /// </summary>
    public string WakeWord { get; set; } = "Hey Kam";
    
    /// <summary>
    /// Detection sensitivity (0.0 to 1.0). Higher values increase detection rate but may cause false positives.
    /// Default: 0.5
    /// </summary>
    public float Sensitivity { get; set; } = 0.5f;
    
    /// <summary>
    /// Path to custom wake word model file (.ppn file for Porcupine).
    /// If not specified, built-in models are used.
    /// </summary>
    public string? CustomModelPath { get; set; }
    
    /// <summary>
    /// Path to custom Porcupine model file (.pv file).
    /// </summary>
    public string? PorcupineModelPath { get; set; }
    
    /// <summary>
    /// Whether wake word detection is enabled. Default: true
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Audio sample rate for wake word detection. Default: 16000
    /// </summary>
    public int SampleRate { get; set; } = 16000;
    
    /// <summary>
    /// Frame length in milliseconds. Default: 32
    /// </summary>
    public int FrameLengthMs { get; set; } = 32;
    
    /// <summary>
    /// Access key for Porcupine (if using custom models).
    /// </summary>
    public string? AccessKey { get; set; }
    
    /// <summary>
    /// List of built-in wake words available.
    /// </summary>
    public static readonly string[] BuiltInWakeWords = new[]
    {
        "Hey Kam",
        "OK Assistant",
        "Hey Computer",
        "Jarvis",
        "Alexa",  // For testing purposes
        "Hey Google",  // For testing purposes
    };
}
