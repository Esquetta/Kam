using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

#pragma warning disable CS0067

/// <summary>
/// Fakes for the voice stack, so the voice assistant and the main window can be tested without a microphone.
/// </summary>
internal sealed class VoiceFakes
{
    public VoiceFakes(Dictionary<string, string?>? configuration = null)
    {
        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configuration ?? new Dictionary<string, string?>())
            .Build();
    }

    public FakeRecorderFactory Recorders { get; } = new();

    public FakeSpeechToText SpeechToText { get; } = new();

    public FakeWakeWord WakeWord { get; } = new();

    public FakeModelStore Models { get; } = new();

    public FakeSpeech Speech { get; } = new();

    public IConfiguration Configuration { get; }

    public VoiceAssistant CreateAssistant()
    {
        return new VoiceAssistant(Recorders, SpeechToText, WakeWord, Models, Speech, Configuration);
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting until {because}.");
            }

            await Task.Delay(10);
        }
    }
}

internal sealed class FakeRecorderFactory : IVoiceRecognitionFactory
{
    public List<FakeRecorder> Created { get; } = [];

    public Exception? StartFailure { get; set; }

    /// <summary>
    /// Audio that <see cref="FakeRecorder.StopListening"/> delivers, as a recorder does with speech in progress.
    /// </summary>
    public byte[]? AudioOnStop { get; set; }

    public FakeRecorder Last => Created[^1];

    public IVoiceRecognitionService Create()
    {
        var recorder = new FakeRecorder(this);
        lock (Created)
        {
            Created.Add(recorder);
        }

        return recorder;
    }
}

internal sealed class FakeRecorder(FakeRecorderFactory factory) : IVoiceRecognitionService
{
    public bool IsListening { get; private set; }

    public bool IsDisposed { get; private set; }

    public event EventHandler<byte[]>? OnVoiceCaptured;
    public event EventHandler<Exception>? OnError;
    public event EventHandler? OnListeningStarted;
    public event EventHandler? OnListeningStopped;
    public event EventHandler<float>? OnAudioLevel;

    public void StartListening()
    {
        if (factory.StartFailure is { } failure)
        {
            throw failure;
        }

        IsListening = true;
        OnListeningStarted?.Invoke(this, EventArgs.Empty);
    }

    public void StopListening()
    {
        if (!IsListening)
        {
            return;
        }

        IsListening = false;
        if (factory.AudioOnStop is { } audio)
        {
            OnVoiceCaptured?.Invoke(this, audio);
        }

        OnListeningStopped?.Invoke(this, EventArgs.Empty);
    }

    public void Speak(byte[] audio)
    {
        OnAudioLevel?.Invoke(this, 0.4f);
        OnVoiceCaptured?.Invoke(this, audio);
    }

    public void Fail(Exception error) => OnError?.Invoke(this, error);

    public void ClearBuffer()
    {
    }

    public long GetCurrentBufferSize() => 0;

    public Task<byte[]> RecordForDurationAsync(TimeSpan duration) => Task.FromResult(Array.Empty<byte>());

    public void Dispose()
    {
        IsListening = false;
        IsDisposed = true;
    }
}

internal sealed class FakeSpeechToText : IMultiSTTService
{
    public string Text { get; set; } = "open spotify";

    public string ErrorMessage { get; set; } = string.Empty;

    public List<byte[]> Received { get; } = [];

    public event EventHandler<ProviderFallbackEventArgs>? OnProviderFallback;
    public event EventHandler<ProviderHealthChangedEventArgs>? OnProviderHealthChanged;

    public Task<MultiSTTResult> ConvertToTextAsync(
        byte[] audioData,
        STTProvider? preferredProvider = null,
        CancellationToken cancellationToken = default)
    {
        lock (Received)
        {
            Received.Add(audioData);
        }

        return Task.FromResult(new MultiSTTResult { Text = Text, ErrorMessage = ErrorMessage, Confidence = 0.9f });
    }

    public Task<MultiSTTResult> ConvertToTextStreamingAsync(
        byte[] audioData,
        Action<string> onInterimResult,
        CancellationToken cancellationToken = default)
    {
        return ConvertToTextAsync(audioData, null, cancellationToken);
    }

    public Dictionary<STTProvider, ProviderHealthStatus> GetProviderHealthStatus() => [];

    public void SetProviderPriority(STTProvider provider, STTProviderPriority priority)
    {
    }

    public Task<TestConnectionResult[]> TestAllProvidersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Array.Empty<TestConnectionResult>());
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeWakeWord : IWakeWordDetectionService
{
    private int _startCount;

    public bool IsListening { get; private set; }

    public string WakeWord { get; private set; } = "Hey Kam";

    public float Sensitivity { get; set; } = 0.5f;

    public int StartCount => Volatile.Read(ref _startCount);

    public Exception? StartFailure { get; set; }

    public event EventHandler<WakeWordDetectedEventArgs>? OnWakeWordDetected;
    public event EventHandler<Exception>? OnError;

    public void StartListening()
    {
        if (StartFailure is { } failure)
        {
            throw failure;
        }

        Interlocked.Increment(ref _startCount);
        IsListening = true;
    }

    public void StopListening()
    {
        IsListening = false;
    }

    public bool SetWakeWord(string wakeWord)
    {
        WakeWord = wakeWord;
        return true;
    }

    public void Detect(string followingText = "", byte[]? utterance = null)
    {
        OnWakeWordDetected?.Invoke(this, new WakeWordDetectedEventArgs(WakeWord, 0.9f)
        {
            FollowingText = followingText,
            Utterance = utterance ?? []
        });
    }

    public void Fail(Exception error) => OnError?.Invoke(this, error);

    public void Dispose()
    {
    }
}

internal sealed class FakeModelStore : ISpeechModelStore
{
    public HashSet<string> Downloaded { get; } = new(StringComparer.OrdinalIgnoreCase) { "tiny", "base" };

    public Exception? DownloadFailure { get; set; }

    public List<string> DownloadRequests { get; } = [];

    public string ModelsDirectory => Path.Combine(Path.GetTempPath(), "kam-test-models");

    public string GetModelPath(string model) => Path.Combine(ModelsDirectory, $"ggml-{model}.bin");

    public bool IsDownloaded(string model)
    {
        lock (Downloaded)
        {
            return Downloaded.Contains(model);
        }
    }

    public Task<string> EnsureModelAsync(string model, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        lock (DownloadRequests)
        {
            DownloadRequests.Add(model);
        }

        if (DownloadFailure is { } failure)
        {
            return Task.FromException<string>(failure);
        }

        progress?.Report(0.5);
        progress?.Report(1);
        lock (Downloaded)
        {
            Downloaded.Add(model);
        }

        return Task.FromResult(GetModelPath(model));
    }
}

internal sealed class FakeSpeech : ITextToSpeechService
{
    private TaskCompletionSource? _speaking;

    public bool IsAvailable { get; set; } = true;

    public bool IsSpeaking => _speaking is { Task.IsCompleted: false };

    public List<string> Spoken { get; } = [];

    public int StopCount { get; private set; }

    public IReadOnlyList<SpeechVoiceInfo> GetVoices() => [new("tr-voice", "Türkçe", "tr"), new("en-voice", "English", "en")];

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        lock (Spoken)
        {
            Spoken.Add(text);
        }

        _speaking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _speaking.Task;
    }

    public void FinishSpeaking() => _speaking?.TrySetResult();

    public void Stop()
    {
        StopCount++;
        _speaking?.TrySetResult();
    }

    public void Dispose()
    {
    }
}

#pragma warning restore CS0067
