using System.Buffers.Binary;

namespace SmartVoiceAgent.Tests.Infrastructure.Services.Voice;

/// <summary>
/// Builds 16 kHz mono 16-bit test audio.
/// </summary>
internal static class AudioTestSignals
{
    public const int SampleRate = 16000;

    public static byte[] Silence(int milliseconds, double amplitude = 0.0005, int seed = 1)
    {
        var random = new Random(seed);
        return Samples(milliseconds, _ => (random.NextDouble() * 2 - 1) * amplitude);
    }

    public static byte[] Tone(int milliseconds, double amplitude = 0.2, double frequency = 220)
    {
        return Samples(milliseconds, i => Math.Sin(2 * Math.PI * frequency * i / SampleRate) * amplitude);
    }

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

    private static byte[] Samples(int milliseconds, Func<int, double> sample)
    {
        var count = SampleRate * milliseconds / 1000;
        var bytes = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var value = (short)Math.Clamp(sample(i) * 32767, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), value);
        }

        return bytes;
    }
}
