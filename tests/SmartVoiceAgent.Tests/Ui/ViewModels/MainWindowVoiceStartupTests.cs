using FluentAssertions;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

#pragma warning disable CS0067

public sealed class MainWindowVoiceStartupTests
{
    [Fact]
    public void SetVoiceCommandService_DoesNotStartWakeWordDetectionByDefault()
    {
        var wakeWord = new RecordingWakeWordDetectionService();
        var voiceService = CreateVoiceCommandService(wakeWord);
        var viewModel = new MainWindowViewModel();

        viewModel.SetVoiceCommandService(voiceService);

        viewModel.IsVoiceEnabled.Should().BeFalse();
        viewModel.IsListeningForWakeWord.Should().BeFalse();
        viewModel.VoiceStatusText.Should().Be("Voice: Off");
        wakeWord.StartListeningCallCount.Should().Be(0);
    }

    [Fact]
    public void ToggleVoiceCommand_StartsWakeWordDetectionOnDemand()
    {
        var wakeWord = new RecordingWakeWordDetectionService();
        var voiceService = CreateVoiceCommandService(wakeWord);
        var viewModel = new MainWindowViewModel();
        viewModel.SetVoiceCommandService(voiceService);

        viewModel.ToggleVoiceCommand.Execute(null);

        viewModel.IsVoiceEnabled.Should().BeTrue();
        wakeWord.StartListeningCallCount.Should().Be(1);
    }

    private static VoiceCommandService CreateVoiceCommandService(RecordingWakeWordDetectionService wakeWord)
    {
        return new VoiceCommandService(
            wakeWord,
            new StubVoiceRecognitionFactory(),
            new StubMultiSttService(),
            new StubNoiseSuppressionService(),
            new StubCommandInputService(),
            new StubUiLogService());
    }

    private sealed class RecordingWakeWordDetectionService : IWakeWordDetectionService
    {
        public bool IsListening { get; private set; }
        public string WakeWord { get; private set; } = "Hey Kam";
        public float Sensitivity { get; set; } = 0.6f;
        public int StartListeningCallCount { get; private set; }

        public event EventHandler<WakeWordDetectedEventArgs>? OnWakeWordDetected;
        public event EventHandler<Exception>? OnError;

        public void StartListening()
        {
            StartListeningCallCount++;
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

        public void Dispose()
        {
        }
    }

    private sealed class StubVoiceRecognitionFactory : IVoiceRecognitionFactory
    {
        public IVoiceRecognitionService Create()
        {
            return new StubVoiceRecognitionService();
        }
    }

    private sealed class StubVoiceRecognitionService : IVoiceRecognitionService
    {
        public bool IsListening { get; private set; }

        public event EventHandler<byte[]>? OnVoiceCaptured;
        public event EventHandler<Exception>? OnError;
        public event EventHandler? OnListeningStarted;
        public event EventHandler? OnListeningStopped;

        public void StartListening()
        {
            IsListening = true;
            OnListeningStarted?.Invoke(this, EventArgs.Empty);
        }

        public void StopListening()
        {
            IsListening = false;
            OnListeningStopped?.Invoke(this, EventArgs.Empty);
        }

        public void ClearBuffer()
        {
        }

        public long GetCurrentBufferSize()
        {
            return 0;
        }

        public Task<byte[]> RecordForDurationAsync(TimeSpan duration)
        {
            return Task.FromResult(Array.Empty<byte>());
        }

        public void Dispose()
        {
        }
    }

    private sealed class StubMultiSttService : IMultiSTTService
    {
        public event EventHandler<ProviderFallbackEventArgs>? OnProviderFallback;
        public event EventHandler<ProviderHealthChangedEventArgs>? OnProviderHealthChanged;

        public Task<MultiSTTResult> ConvertToTextAsync(
            byte[] audioData,
            STTProvider? preferredProvider = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MultiSTTResult());
        }

        public Task<MultiSTTResult> ConvertToTextStreamingAsync(
            byte[] audioData,
            Action<string> onInterimResult,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MultiSTTResult());
        }

        public Dictionary<STTProvider, ProviderHealthStatus> GetProviderHealthStatus()
        {
            return [];
        }

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

    private sealed class StubNoiseSuppressionService : INoiseSuppressionService
    {
        public bool IsInitialized => true;

        public byte[] SuppressNoise(byte[] audioData, int sampleRate = 16000)
        {
            return audioData;
        }

        public byte[] SuppressNoise(byte[] audioData, NoiseSuppressionOptions options)
        {
            return audioData;
        }

        public float EstimateNoiseLevel(byte[] audioData, int sampleRate = 16000)
        {
            return 0;
        }

        public byte[] ApplyAGC(byte[] audioData, float targetLevel = 0.3f)
        {
            return audioData;
        }

        public byte[] RemoveEcho(byte[] audioData, byte[] referenceData, int sampleRate = 16000)
        {
            return audioData;
        }
    }

    private sealed class StubCommandInputService : ICommandInputService
    {
        public event EventHandler<CommandResultEventArgs>? OnResult;

        public void SubmitCommand(string command)
        {
        }

        public Task<string> ReadCommandAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(string.Empty);
        }

        public void PublishResult(string command, string result, bool success = true)
        {
            OnResult?.Invoke(this, new CommandResultEventArgs
            {
                Command = command,
                Result = result,
                Success = success
            });
        }
    }

    private sealed class StubUiLogService : IUiLogService
    {
        public event EventHandler<UiLogEntry>? OnLogEntry;

        public void Log(string message, LogLevel level = LogLevel.Information, string? source = null)
        {
            OnLogEntry?.Invoke(this, new UiLogEntry
            {
                Message = message,
                Level = level,
                Source = source
            });
        }

        public void LogAgentUpdate(string agentName, string message, bool isComplete = false)
        {
        }
    }
}

#pragma warning restore CS0067
