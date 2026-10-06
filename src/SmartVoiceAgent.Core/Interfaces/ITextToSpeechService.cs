namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Reads text aloud with the voice, rate and output device chosen in Settings.
/// </summary>
public interface ITextToSpeechService : IDisposable
{
    /// <summary>
    /// Gets whether this computer can speak.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets whether text is being read now.
    /// </summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Returns the installed voices.
    /// </summary>
    IReadOnlyList<SpeechVoiceInfo> GetVoices();

    /// <summary>
    /// Reads <paramref name="text"/> aloud and completes when it is done or stopped. A new call stops the previous one.
    /// </summary>
    /// <param name="text">Plain text to read.</param>
    /// <param name="cancellationToken">Stops reading.</param>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops reading.
    /// </summary>
    void Stop();
}

/// <summary>
/// An installed voice.
/// </summary>
/// <param name="Id">The id Settings store.</param>
/// <param name="Name">The voice's display name.</param>
/// <param name="Language">The voice's two-letter language, such as <c>tr</c>, or empty when unknown.</param>
public sealed record SpeechVoiceInfo(string Id, string Name, string Language)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}
