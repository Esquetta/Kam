using System.Buffers.Binary;
using System.Text;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// Reads and writes the audio speech recognition passes around: raw 16 kHz mono 16-bit PCM, or the same in a
/// WAV container.
/// </summary>
public static class WaveAudio
{
    /// <summary>
    /// The sample rate speech recognition works at.
    /// </summary>
    public const int SampleRate = 16000;

    /// <summary>
    /// Returns whether <paramref name="audio"/> starts with a RIFF/WAVE header.
    /// </summary>
    /// <param name="audio">The audio bytes.</param>
    public static bool IsWave(ReadOnlySpan<byte> audio)
    {
        return audio.Length >= 12
            && audio[..4].SequenceEqual("RIFF"u8)
            && audio.Slice(8, 4).SequenceEqual("WAVE"u8);
    }

    /// <summary>
    /// Returns the samples of raw PCM or WAV audio as 16 kHz mono floats in -1..1.
    /// </summary>
    /// <param name="audio">Raw 16 kHz mono 16-bit PCM, or a WAV file in any PCM or float layout.</param>
    public static float[] ToSamples(ReadOnlySpan<byte> audio)
    {
        var pcm = IsWave(audio) ? ReadWaveAsPcm16(audio) : audio[..(audio.Length & ~1)].ToArray();
        var samples = new float[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2)) / 32768f;
        }

        return samples;
    }

    /// <summary>
    /// Returns WAV bytes for raw 16 kHz mono 16-bit PCM; WAV input is returned as is.
    /// </summary>
    /// <param name="audio">The audio.</param>
    public static byte[] ToWave(byte[] audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (IsWave(audio))
        {
            return audio;
        }

        var dataLength = audio.Length & ~1;
        var wave = new byte[44 + dataLength];
        var span = wave.AsSpan();
        Encoding.ASCII.GetBytes("RIFF", span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataLength);
        Encoding.ASCII.GetBytes("WAVE", span[8..]);
        Encoding.ASCII.GetBytes("fmt ", span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        Encoding.ASCII.GetBytes("data", span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataLength);
        audio.AsSpan(0, dataLength).CopyTo(span[44..]);
        return wave;
    }

    /// <summary>
    /// Returns the length of raw 16 kHz mono 16-bit PCM.
    /// </summary>
    /// <param name="pcmBytes">The byte count.</param>
    public static TimeSpan Duration(long pcmBytes) => TimeSpan.FromSeconds(pcmBytes / 2.0 / SampleRate);

    private static byte[] ReadWaveAsPcm16(ReadOnlySpan<byte> wave)
    {
        int? sampleRate = null, channels = null, bits = null;
        var isFloat = false;
        var offset = 12;
        while (offset + 8 <= wave.Length)
        {
            var id = wave.Slice(offset, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(wave[(offset + 4)..]);
            var body = offset + 8;
            if (size < 0 || body > wave.Length)
            {
                break;
            }

            var available = Math.Min(size, wave.Length - body);
            if (id.SequenceEqual("fmt "u8) && available >= 16)
            {
                var format = BinaryPrimitives.ReadUInt16LittleEndian(wave[body..]);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wave[(body + 2)..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(wave[(body + 4)..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(wave[(body + 14)..]);
                isFloat = format == 3
                    || (format == 0xFFFE && available >= 26 && BinaryPrimitives.ReadUInt16LittleEndian(wave[(body + 24)..]) == 3);
            }
            else if (id.SequenceEqual("data"u8))
            {
                var data = wave.Slice(body, available);
                if (sampleRate is null || channels is null || bits is null)
                {
                    throw new InvalidDataException("The WAV file has no format chunk before its data.");
                }

                var converter = new PcmConverter(sampleRate.Value, channels.Value, bits.Value, isFloat, SampleRate);
                return converter.Convert(data);
            }

            offset = body + size + (size & 1);
        }

        throw new InvalidDataException("The WAV file has no data chunk.");
    }
}
