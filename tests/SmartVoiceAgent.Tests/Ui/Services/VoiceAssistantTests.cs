using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Services;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class VoiceAssistantTests
{
    private static readonly byte[] Speech = new byte[16_000];

    [Fact]
    public async Task ToggleTalkAsync_RecordsAndRoutesTheFirstUtterance()
    {
        var fakes = new VoiceFakes();
        using var voice = fakes.CreateAssistant();
        var routed = Route(voice);

        await voice.ToggleTalkAsync();

        voice.State.Should().Be(VoiceState.Listening);
        fakes.Recorders.Last.IsListening.Should().BeTrue();

        fakes.Recorders.Last.Speak(Speech);

        await VoiceFakes.WaitUntilAsync(() => routed.Count == 1 && voice.State == VoiceState.Ready, "the command is routed");
        routed.Should().Equal("open spotify");
        fakes.Recorders.Last.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleTalkAsync_WhileListening_SendsSpeechInProgress()
    {
        var fakes = new VoiceFakes();
        fakes.Recorders.AudioOnStop = Speech;
        using var voice = fakes.CreateAssistant();
        var routed = Route(voice);

        await voice.ToggleTalkAsync();
        await voice.ToggleTalkAsync();

        await VoiceFakes.WaitUntilAsync(() => routed.Count == 1, "the command is routed");
        routed.Should().Equal("open spotify");
    }

    [Fact]
    public async Task ToggleTalkAsync_WhenNothingWasSaid_ReportsNothingHeard()
    {
        var fakes = new VoiceFakes();
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);
        var routed = Route(voice);

        await voice.ToggleTalkAsync();
        await voice.ToggleTalkAsync();

        voice.State.Should().Be(VoiceState.Ready);
        Last(events).Problem.Should().Be(VoiceProblem.NothingHeard);
        routed.Should().BeEmpty();
    }

    [Fact]
    public async Task ToggleTalkAsync_DownloadsAMissingModelFirst()
    {
        var fakes = new VoiceFakes();
        fakes.Models.Downloaded.Remove("base");
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);

        await voice.ToggleTalkAsync();

        fakes.Models.DownloadRequests.Should().Equal("base");
        Snapshot(events).Should().Contain(e => e.State == VoiceState.Preparing && e.Progress == 0.5);
        voice.State.Should().Be(VoiceState.Listening);
    }

    [Fact]
    public async Task ToggleTalkAsync_WhenTheModelDownloadFails_ReportsIt()
    {
        var fakes = new VoiceFakes();
        fakes.Models.Downloaded.Remove("base");
        fakes.Models.DownloadFailure = new HttpRequestException("offline");
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);

        await voice.ToggleTalkAsync();

        voice.State.Should().Be(VoiceState.Ready);
        Last(events).Problem.Should().Be(VoiceProblem.ModelDownloadFailed);
        Last(events).Detail.Should().Be("offline");
        fakes.Recorders.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task ToggleTalkAsync_WithTheApiEngineNotSetUp_ReportsNoSpeechEngine()
    {
        var fakes = new VoiceFakes(new Dictionary<string, string?> { ["Voice:SpeechEngine"] = "OpenAI" });
        fakes.Models.Downloaded.Clear();
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);

        await voice.ToggleTalkAsync();

        Last(events).Problem.Should().Be(VoiceProblem.NoSpeechEngine);
        fakes.Recorders.Created.Should().BeEmpty();
        fakes.Models.DownloadRequests.Should().BeEmpty("the API engine never downloads a local model on its own");
    }

    [Fact]
    public async Task ToggleTalkAsync_WithTheApiEngineSetUp_RecordsWithoutALocalModel()
    {
        var fakes = new VoiceFakes(new Dictionary<string, string?>
        {
            ["Voice:SpeechEngine"] = "OpenAI",
            ["Voice:SpeechApi:ApiKey"] = "sk-test"
        });
        fakes.Models.Downloaded.Clear();
        using var voice = fakes.CreateAssistant();

        await voice.ToggleTalkAsync();

        voice.State.Should().Be(VoiceState.Listening);
        fakes.Models.DownloadRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ToggleTalkAsync_WhenTheMicrophoneFails_ReportsIt()
    {
        var fakes = new VoiceFakes();
        fakes.Recorders.StartFailure = new InvalidOperationException("no microphone");
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);

        await voice.ToggleTalkAsync();

        voice.State.Should().Be(VoiceState.Ready);
        Last(events).Problem.Should().Be(VoiceProblem.MicrophoneUnavailable);
        Last(events).Detail.Should().Be("no microphone");
    }

    [Fact]
    public async Task RecorderError_WhileStarting_DoesNotShowListening()
    {
        var fakes = new VoiceFakes();
        fakes.Recorders.ErrorWhileStarting = new InvalidOperationException("device busy");
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);

        await voice.ToggleTalkAsync();

        voice.State.Should().Be(VoiceState.Ready);
        Snapshot(events).Should().NotContain(e => e.State == VoiceState.Listening);
        Last(events).Problem.Should().Be(VoiceProblem.MicrophoneUnavailable);
        Last(events).Detail.Should().Be("device busy");
    }

    [Fact]
    public async Task RecorderError_WhileListening_ReportsMicrophoneUnavailable()
    {
        var fakes = new VoiceFakes();
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);

        await voice.ToggleTalkAsync();
        fakes.Recorders.Last.Fail(new InvalidOperationException("unplugged"));

        voice.State.Should().Be(VoiceState.Ready);
        Last(events).Problem.Should().Be(VoiceProblem.MicrophoneUnavailable);
    }

    [Fact]
    public async Task EmptyTranscript_ReportsNothingHeard()
    {
        var fakes = new VoiceFakes();
        fakes.SpeechToText.Text = string.Empty;
        fakes.SpeechToText.ErrorMessage = TranscriptCleaner.NoSpeechMessage;
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);
        var routed = Route(voice);

        await voice.ToggleTalkAsync();
        fakes.Recorders.Last.Speak(Speech);

        await VoiceFakes.WaitUntilAsync(() => Snapshot(events).Any(e => e.Problem != VoiceProblem.None), "a problem is reported");
        Last(events).Problem.Should().Be(VoiceProblem.NothingHeard);
        routed.Should().BeEmpty();
    }

    [Fact]
    public async Task EngineError_ReportsTranscriptionFailed()
    {
        var fakes = new VoiceFakes();
        fakes.SpeechToText.Text = string.Empty;
        fakes.SpeechToText.ErrorMessage = "HTTP 500";
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);

        await voice.ToggleTalkAsync();
        fakes.Recorders.Last.Speak(Speech);

        await VoiceFakes.WaitUntilAsync(() => Snapshot(events).Any(e => e.Problem != VoiceProblem.None), "a problem is reported");
        Last(events).Problem.Should().Be(VoiceProblem.TranscriptionFailed);
        Last(events).Detail.Should().Be("HTTP 500");
    }

    [Fact]
    public async Task SetWakeWordEnabledAsync_ListensForThePhrase()
    {
        var fakes = new VoiceFakes();
        using var voice = fakes.CreateAssistant();

        await voice.SetWakeWordEnabledAsync(true);

        voice.IsWakeWordEnabled.Should().BeTrue();
        voice.State.Should().Be(VoiceState.WakeListening);
        fakes.WakeWord.IsListening.Should().BeTrue();

        await voice.SetWakeWordEnabledAsync(false);

        voice.State.Should().Be(VoiceState.Ready);
        fakes.WakeWord.IsListening.Should().BeFalse();
    }

    [Fact]
    public async Task SetWakeWordEnabledAsync_WhenTheModelCannotDownload_TurnsTheListenerOff()
    {
        var fakes = new VoiceFakes();
        fakes.Models.Downloaded.Remove("tiny");
        fakes.Models.DownloadFailure = new HttpRequestException("offline");
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);

        await voice.SetWakeWordEnabledAsync(true);

        voice.IsWakeWordEnabled.Should().BeFalse();
        Last(events).Problem.Should().Be(VoiceProblem.WakeWordUnavailable);
        fakes.Models.DownloadRequests.Should().Equal("tiny");
    }

    [Fact]
    public async Task WakePhraseWithACommand_TranscribesTheUtteranceWithoutThePhrase()
    {
        var fakes = new VoiceFakes();
        fakes.SpeechToText.Text = "Hey Kam, open Spotify";
        using var voice = fakes.CreateAssistant();
        var routed = Route(voice);
        await voice.SetWakeWordEnabledAsync(true);

        fakes.WakeWord.Detect("open Spotify", Speech);

        await VoiceFakes.WaitUntilAsync(() => routed.Count == 1, "the command is routed");
        routed.Should().Equal("open Spotify");
        await VoiceFakes.WaitUntilAsync(() => voice.State == VoiceState.WakeListening, "the listener resumes");
        fakes.WakeWord.StartCount.Should().Be(2);
        fakes.Recorders.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task WakePhraseAlone_RecordsTheCommand()
    {
        var fakes = new VoiceFakes();
        using var voice = fakes.CreateAssistant();
        await voice.SetWakeWordEnabledAsync(true);

        fakes.WakeWord.Detect();

        await VoiceFakes.WaitUntilAsync(() => voice.State == VoiceState.Listening, "recording starts");
        fakes.WakeWord.IsListening.Should().BeFalse();
        fakes.Recorders.Created.Should().ContainSingle();
    }

    [Fact]
    public async Task WakeListenerError_TurnsTheListenerOff()
    {
        var fakes = new VoiceFakes();
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);
        await voice.SetWakeWordEnabledAsync(true);

        fakes.WakeWord.Fail(new InvalidOperationException("model missing"));

        voice.IsWakeWordEnabled.Should().BeFalse();
        voice.State.Should().Be(VoiceState.Ready);
        Last(events).Problem.Should().Be(VoiceProblem.WakeWordUnavailable);
    }

    [Fact]
    public async Task SpeakAsync_ReadsThePlainTextAndResumesTheWakeListener()
    {
        var fakes = new VoiceFakes();
        using var voice = fakes.CreateAssistant();
        await voice.SetWakeWordEnabledAsync(true);

        var speaking = voice.SpeakAsync("**Done.** Opened `Spotify`.");

        voice.State.Should().Be(VoiceState.Speaking);
        fakes.WakeWord.IsListening.Should().BeFalse("Kam must not hear its own voice");
        fakes.Speech.Spoken.Should().ContainSingle().Which.Should().NotContain("*").And.NotContain("`");

        fakes.Speech.FinishSpeaking();
        await speaking;

        voice.State.Should().Be(VoiceState.WakeListening);
    }

    [Fact]
    public async Task ToggleTalkAsync_WhileSpeaking_StopsReading()
    {
        var fakes = new VoiceFakes();
        using var voice = fakes.CreateAssistant();
        var speaking = voice.SpeakAsync("Opened Spotify.");

        await voice.ToggleTalkAsync();
        await speaking;

        fakes.Speech.StopCount.Should().Be(1);
        voice.State.Should().Be(VoiceState.Ready);
        fakes.Recorders.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task SpeakAsync_WithoutVoices_DoesNothing()
    {
        var fakes = new VoiceFakes();
        fakes.Speech.IsAvailable = false;
        using var voice = fakes.CreateAssistant();

        await voice.SpeakAsync("Opened Spotify.");

        voice.CanSpeak.Should().BeFalse();
        fakes.Speech.Spoken.Should().BeEmpty();
        voice.State.Should().Be(VoiceState.Ready);
    }

    [Fact]
    public async Task Cancel_WhileListening_DropsTheRecording()
    {
        var fakes = new VoiceFakes();
        using var voice = fakes.CreateAssistant();
        var routed = Route(voice);
        await voice.ToggleTalkAsync();
        var recorder = fakes.Recorders.Last;

        voice.Cancel();
        recorder.Speak(Speech);
        await Task.Delay(50);

        voice.State.Should().Be(VoiceState.Ready);
        await VoiceFakes.WaitUntilAsync(() => recorder.IsDisposed, "the recorder is released");
        routed.Should().BeEmpty();
    }

    [Fact]
    public async Task ToggleTalkAsync_WhenTheMicrophoneWontStop_StopsListeningAnyway()
    {
        var fakes = new VoiceFakes();
        using var stuck = new ManualResetEventSlim(false);
        fakes.Recorders.StopGate = stuck;
        using var voice = fakes.CreateAssistant();
        voice.StopTimeout = TimeSpan.FromMilliseconds(100);
        var events = Track(voice);
        var routed = Route(voice);

        await voice.ToggleTalkAsync();
        var recorder = fakes.Recorders.Last;
        await voice.ToggleTalkAsync();

        voice.State.Should().Be(VoiceState.Ready);
        Last(events).Problem.Should().Be(VoiceProblem.MicrophoneUnavailable);

        stuck.Set();
        await VoiceFakes.WaitUntilAsync(() => recorder.IsDisposed, "the recorder is released once it stops");
        routed.Should().BeEmpty();
    }

    [Fact]
    public async Task ToggleTalkAsync_WhenStoppingFails_ReportsItAndCanTalkAgain()
    {
        var fakes = new VoiceFakes();
        fakes.Recorders.StopFailure = new InvalidOperationException("driver error");
        using var voice = fakes.CreateAssistant();
        var events = Track(voice);

        await voice.ToggleTalkAsync();
        await voice.ToggleTalkAsync();

        voice.State.Should().Be(VoiceState.Ready);
        Last(events).Problem.Should().Be(VoiceProblem.MicrophoneUnavailable);
        Last(events).Detail.Should().Be("driver error");

        fakes.Recorders.StopFailure = null;
        await voice.ToggleTalkAsync();

        voice.State.Should().Be(VoiceState.Listening);
    }

    [Fact]
    public async Task Recording_GoesToSpeechToTextUnchanged()
    {
        var fakes = new VoiceFakes();
        using var voice = fakes.CreateAssistant();
        Route(voice);

        await voice.ToggleTalkAsync();
        fakes.Recorders.Last.Speak(Speech);

        await VoiceFakes.WaitUntilAsync(() => fakes.SpeechToText.Received.Count == 1, "the recording is transcribed");
        fakes.SpeechToText.Received[0].Should().BeSameAs(Speech, "noise suppression garbles speech Whisper would get right");
    }

    private static List<string> Route(VoiceAssistant voice)
    {
        var routed = new List<string>();
        voice.CommandRouter = text =>
        {
            lock (routed)
            {
                routed.Add(text);
            }

            return true;
        };
        return routed;
    }

    private static List<VoiceStateChangedEventArgs> Track(VoiceAssistant voice)
    {
        var events = new List<VoiceStateChangedEventArgs>();
        voice.StateChanged += (_, e) =>
        {
            lock (events)
            {
                events.Add(e);
            }
        };
        return events;
    }

    private static List<VoiceStateChangedEventArgs> Snapshot(List<VoiceStateChangedEventArgs> events)
    {
        lock (events)
        {
            return [.. events];
        }
    }

    private static VoiceStateChangedEventArgs Last(List<VoiceStateChangedEventArgs> events) => Snapshot(events)[^1];
}
