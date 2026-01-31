namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Service interface for wake word (hotword) detection.
/// Listens continuously for a specific wake word and triggers events when detected.
/// </summary>
public interface IWakeWordDetectionService : IDisposable
{
    /// <summary>
    /// Gets whether the wake word detector is currently active and listening.
    /// </summary>
    bool IsListening { get; }
    
    /// <summary>
    /// Gets the name of the currently configured wake word.
    /// </summary>
    string WakeWord { get; }
    
    /// <summary>
    /// Gets or sets the detection sensitivity (0.0 to 1.0, where higher is more sensitive).
    /// </summary>
    float Sensitivity { get; set; }

    /// <summary>
    /// Event raised when the wake word is detected.
    /// </summary>
    event EventHandler<WakeWordDetectedEventArgs>? OnWakeWordDetected;

    /// <summary>
    /// Event raised when an error occurs during detection.
    /// </summary>
    event EventHandler<Exception>? OnError;

    /// <summary>
    /// Starts listening for the wake word continuously.
    /// </summary>
    void StartListening();

    /// <summary>
    /// Stops listening for the wake word.
    /// </summary>
    void StopListening();

    /// <summary>
    /// Changes the wake word to a different keyword.
    /// </summary>
    /// <param name="wakeWord">The new wake word to detect (e.g., "Hey Kam", "OK Assistant")</param>
    /// <returns>True if the wake word was changed successfully, false otherwise.</returns>
    bool SetWakeWord(string wakeWord);
}

/// <summary>
/// Event arguments for wake word detection events.
/// </summary>
public class WakeWordDetectedEventArgs : System.EventArgs
{
    /// <summary>
    /// The wake word that was detected.
    /// </summary>
    public string WakeWord { get; }
    
    /// <summary>
    /// The confidence score of the detection (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; }
    
    /// <summary>
    /// The direction of arrival (in degrees) if available from microphone array.
    /// </summary>
    public int? DirectionOfArrival { get; }
    
    /// <summary>
    /// Timestamp when the wake word was detected.
    /// </summary>
    public DateTime DetectedAt { get; }

    public WakeWordDetectedEventArgs(string wakeWord, float confidence, int? directionOfArrival = null)
    {
        WakeWord = wakeWord;
        Confidence = confidence;
        DirectionOfArrival = directionOfArrival;
        DetectedAt = DateTime.UtcNow;
    }
}
