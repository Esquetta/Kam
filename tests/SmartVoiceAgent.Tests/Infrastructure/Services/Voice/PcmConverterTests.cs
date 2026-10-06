using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Services.Voice;
using System.Buffers.Binary;

namespace SmartVoiceAgent.Tests.Infrastructure.Services.Voice;

public sealed class PcmConverterTests
{
    [Fact]
    public void Convert_DownmixesAndResamplesFloatStereo()
    {
        const int inputRate = 48000;
        var input = new byte[inputRate * 2 * 4];
        for (var i = 0; i < inputRate; i++)
        {
            var value = (float)(Math.Sin(2 * Math.PI * 440 * i / inputRate) * 0.5);
            BinaryPrimitives.WriteSingleLittleEndian(input.AsSpan(i * 8), value);
            BinaryPrimitives.WriteSingleLittleEndian(input.AsSpan(i * 8 + 4), value);
        }

        var converter = new PcmConverter(inputRate, 2, 32, isFloat: true);
        var output = new List<byte>();
        for (var offset = 0; offset < input.Length; offset += 4800 * 8)
        {
            output.AddRange(converter.Convert(input.AsSpan(offset, Math.Min(4800 * 8, input.Length - offset))));
        }

        (output.Count / 2).Should().BeInRange(15500, 16100);
        var samples = Enumerable.Range(0, output.Count / 2)
            .Select(i => BinaryPrimitives.ReadInt16LittleEndian(output.ToArray().AsSpan(i * 2)) / 32768.0)
            .Skip(200)
            .ToArray();
        samples.Max().Should().BeApproximately(0.5, 0.05);
        var crossings = samples.Zip(samples.Skip(1)).Count(pair => pair.First < 0 && pair.Second >= 0);
        crossings.Should().BeInRange(420, 445, "a 440 Hz tone keeps its pitch");
    }

    [Fact]
    public void Convert_PassesThroughSpeechFormat()
    {
        var converter = new PcmConverter(16000, 1, 16, isFloat: false);

        converter.IsPassThrough.Should().BeTrue();
        converter.Convert(new byte[] { 1, 2, 3, 4, 5 }).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Convert_Reads24BitPcm()
    {
        var converter = new PcmConverter(16000, 1, 24, isFloat: false);
        var input = new byte[] { 0x00, 0x00, 0x40, 0x00, 0x00, 0xC0 };

        var output = converter.Convert(input);

        BinaryPrimitives.ReadInt16LittleEndian(output).Should().BeCloseTo(16384, 2);
        BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(2)).Should().BeCloseTo(-16384, 2);
    }

    [Fact]
    public void Constructor_RejectsUnsupportedFormats()
    {
        var act = () => new PcmConverter(16000, 1, 8, isFloat: false);

        act.Should().Throw<NotSupportedException>();
    }
}
