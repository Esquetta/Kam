using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Services.Voice;
using static SmartVoiceAgent.Tests.Infrastructure.Services.Voice.AudioTestSignals;

namespace SmartVoiceAgent.Tests.Infrastructure.Services.Voice;

public sealed class WaveAudioTests
{
    [Fact]
    public void ToWave_RoundTripsThroughToSamples()
    {
        var pcm = Tone(250, amplitude: 0.25);

        var wave = WaveAudio.ToWave(pcm);

        WaveAudio.IsWave(wave).Should().BeTrue();
        wave.Length.Should().Be(pcm.Length + 44);
        WaveAudio.ToSamples(wave).Should().Equal(WaveAudio.ToSamples(pcm));
        WaveAudio.ToWave(wave).Should().BeSameAs(wave);
    }

    [Fact]
    public void ToSamples_ReadsRawPcmWithoutSkippingBytes()
    {
        var pcm = new byte[] { 0x00, 0x40, 0x00, 0xC0 };

        WaveAudio.ToSamples(pcm).Should().Equal(0.5f, -0.5f);
    }

    [Fact]
    public void ToSamples_ResamplesWaveFilesInOtherFormats()
    {
        var wave = BuildWave(sampleRate: 8000, channels: 2, seconds: 1);

        var samples = WaveAudio.ToSamples(wave);

        samples.Length.Should().BeInRange(15800, 16100);
    }

    [Fact]
    public void ToSamples_RejectsWaveWithoutData()
    {
        var header = WaveAudio.ToWave([])[..36];

        var act = () => WaveAudio.ToSamples(header);

        act.Should().Throw<InvalidDataException>();
    }

    private static byte[] BuildWave(int sampleRate, int channels, int seconds)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataLength = sampleRate * channels * 2 * seconds;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
        return stream.ToArray();
    }
}
