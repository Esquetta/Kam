using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Services.Voice;
using static SmartVoiceAgent.Tests.Infrastructure.Services.Voice.AudioTestSignals;

namespace SmartVoiceAgent.Tests.Infrastructure.Services.Voice;

public sealed class VoiceActivityDetectorTests
{
    [Fact]
    public void Process_ReturnsUtteranceAfterSpeechAndSilence()
    {
        var detector = new VoiceActivityDetector();

        var utterances = Feed(detector, Concat(Silence(1000), Tone(700), Silence(1200)), chunkMs: 100);

        utterances.Should().ContainSingle();
        var duration = utterances[0].Length / 2.0 / SampleRate;
        duration.Should().BeInRange(0.7 + 0.25, 0.7 + 0.3 + 0.9, "the pre-roll and the closing silence stay with the speech");
        detector.InSpeech.Should().BeFalse();
    }

    [Fact]
    public void Process_IgnoresSteadyBackgroundNoise()
    {
        var detector = new VoiceActivityDetector();

        var utterances = Feed(detector, Silence(5000, amplitude: 0.003), chunkMs: 128);

        utterances.Should().BeEmpty();
        detector.NoiseFloor.Should().BeGreaterThan(0.0005);
    }

    [Fact]
    public void Process_DropsClicksShorterThanMinimumSpeech()
    {
        var detector = new VoiceActivityDetector();

        var utterances = Feed(detector, Concat(Silence(500), Tone(140), Silence(1500)), chunkMs: 20);

        utterances.Should().BeEmpty();
    }

    [Fact]
    public void Process_SplitsTwoUtterancesSeparatedByAPause()
    {
        var detector = new VoiceActivityDetector();

        var utterances = Feed(detector, Concat(Silence(400), Tone(500), Silence(1000), Tone(600), Silence(1000)), chunkMs: 100);

        utterances.Should().HaveCount(2);
    }

    [Fact]
    public void Flush_ReturnsSpeechStillInProgress()
    {
        var detector = new VoiceActivityDetector();
        Feed(detector, Concat(Silence(300), Tone(600)), chunkMs: 50).Should().BeEmpty();

        detector.InSpeech.Should().BeTrue();
        detector.Flush().Should().NotBeNull();
        detector.InSpeech.Should().BeFalse();
    }

    [Fact]
    public void Process_CutsUtteranceAtMaximumLength()
    {
        var detector = new VoiceActivityDetector(new VoiceActivityOptions { MaxUtteranceMs = 1000 });

        var utterances = Feed(detector, Concat(Silence(200), Tone(2500)), chunkMs: 100);

        utterances.Should().NotBeEmpty();
        utterances[0].Length.Should().BeLessThanOrEqualTo(SampleRate * 2 * 1000 / 1000 + 640);
    }

    [Fact]
    public void Rms_IsScaledToOne()
    {
        VoiceActivityDetector.Rms(Tone(100, amplitude: 0.5)).Should().BeApproximately(0.5 / Math.Sqrt(2), 0.01);
        VoiceActivityDetector.Rms([]).Should().Be(0);
    }

    private static List<byte[]> Feed(VoiceActivityDetector detector, byte[] audio, int chunkMs)
    {
        var chunk = SampleRate * chunkMs / 1000 * 2;
        var utterances = new List<byte[]>();
        for (var offset = 0; offset < audio.Length; offset += chunk)
        {
            utterances.AddRange(detector.Process(audio.AsSpan(offset, Math.Min(chunk, audio.Length - offset))));
        }

        return utterances;
    }
}
