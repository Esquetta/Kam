using NAudio.Dsp;
using System.Buffers.Binary;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// Converts captured audio in any common PCM or float layout to the 16-bit mono stream speech recognition
/// expects. Keeps resampler state between calls, so feed one capture's buffers in order.
/// </summary>
public sealed class PcmConverter
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _bytesPerSample;
    private readonly bool _isFloat;
    private readonly int _targetRate;
    private readonly WdlResampler? _resampler;

    /// <summary>
    /// Creates a converter.
    /// </summary>
    /// <param name="sampleRate">The input sample rate.</param>
    /// <param name="channels">The input channel count.</param>
    /// <param name="bitsPerSample">The input sample size: 16, 24 or 32.</param>
    /// <param name="isFloat">Whether 32-bit input samples are IEEE floats.</param>
    /// <param name="targetRate">The output sample rate.</param>
    public PcmConverter(int sampleRate, int channels, int bitsPerSample, bool isFloat, int targetRate = 16000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        if (bitsPerSample is not (16 or 24 or 32) || (isFloat && bitsPerSample != 32))
        {
            throw new NotSupportedException($"Unsupported capture format: {bitsPerSample}-bit {(isFloat ? "float" : "PCM")}.");
        }

        _sampleRate = sampleRate;
        _channels = channels;
        _bytesPerSample = bitsPerSample / 8;
        _isFloat = isFloat;
        _targetRate = targetRate;

        if (sampleRate != targetRate)
        {
            _resampler = new WdlResampler();
            _resampler.SetMode(true, 2, false, 64, 32);
            _resampler.SetFilterParms(0.693f, 0.707f);
            _resampler.SetFeedMode(true);
            _resampler.SetRates(sampleRate, targetRate);
        }
    }

    /// <summary>
    /// Gets whether the input already is 16-bit mono at the target rate.
    /// </summary>
    public bool IsPassThrough => _resampler is null && _channels == 1 && _bytesPerSample == 2 && !_isFloat;

    /// <summary>
    /// Converts one captured buffer.
    /// </summary>
    /// <param name="input">Interleaved samples in the input layout.</param>
    /// <returns>16-bit little-endian mono samples at the target rate.</returns>
    public byte[] Convert(ReadOnlySpan<byte> input)
    {
        if (IsPassThrough)
        {
            return input[..(input.Length & ~1)].ToArray();
        }

        var frameBytes = _bytesPerSample * _channels;
        var frames = input.Length / frameBytes;
        if (frames == 0)
        {
            return [];
        }

        var mono = new float[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            float sum = 0;
            var offset = frame * frameBytes;
            for (var channel = 0; channel < _channels; channel++)
            {
                sum += ReadSample(input.Slice(offset + channel * _bytesPerSample, _bytesPerSample));
            }

            mono[frame] = sum / _channels;
        }

        if (_resampler is null)
        {
            return ToPcm16(mono, mono.Length);
        }

        var inNeeded = _resampler.ResamplePrepare(frames, 1, out var inBuffer, out var inOffset);
        Array.Copy(mono, 0, inBuffer, inOffset, Math.Min(frames, inNeeded));
        var maxOut = (int)((long)frames * _targetRate / _sampleRate) + 32;
        var output = new float[maxOut];
        var produced = _resampler.ResampleOut(output, 0, Math.Min(frames, inNeeded), maxOut, 1);
        return ToPcm16(output, produced);
    }

    private float ReadSample(ReadOnlySpan<byte> sample)
    {
        return _bytesPerSample switch
        {
            2 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
            3 => ((sample[0] | (sample[1] << 8) | ((sbyte)sample[2] << 16)) / 8388608f),
            _ => _isFloat
                ? BinaryPrimitives.ReadSingleLittleEndian(sample)
                : BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f
        };
    }

    private static byte[] ToPcm16(float[] samples, int count)
    {
        var bytes = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var value = (short)Math.Clamp(MathF.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), value);
        }

        return bytes;
    }
}
