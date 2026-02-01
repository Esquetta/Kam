using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Infrastructure.Services.Audio;

/// <summary>
/// Noise suppression service using spectral subtraction and filtering techniques.
/// </summary>
public class NoiseSuppressionService : INoiseSuppressionService
{
    private readonly ILogger<NoiseSuppressionService> _logger;
    private bool _isInitialized;
    private readonly object _lock = new();

    // FFT size for spectral processing
    private const int FftSize = 512;
    private const int HopSize = 256; // 50% overlap

    // Pre-emphasis coefficient
    private const float PreEmphasisCoeff = 0.97f;

    public bool IsInitialized => _isInitialized;

    public NoiseSuppressionService(ILogger<NoiseSuppressionService> logger)
    {
        _logger = logger;
        _isInitialized = true;
        _logger.LogDebug("NoiseSuppressionService initialized");
    }

    public byte[] SuppressNoise(byte[] audioData, int sampleRate = 16000)
    {
        return SuppressNoise(audioData, new NoiseSuppressionOptions { SampleRate = sampleRate });
    }

    public byte[] SuppressNoise(byte[] audioData, NoiseSuppressionOptions options)
    {
        if (audioData == null || audioData.Length < 2)
            return audioData ?? Array.Empty<byte>();

        // Ensure even number of bytes (16-bit PCM)
        if (audioData.Length % 2 != 0)
        {
            _logger.LogWarning("Audio data has odd length ({Length}), truncating to even", audioData.Length);
            Array.Resize(ref audioData, audioData.Length - 1);
        }

        var stopwatch = global::System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            lock (_lock)
            {
                _logger.LogDebug("Starting noise suppression on {Length} bytes ({Samples} samples)", 
                    audioData.Length, audioData.Length / 2);

                // Convert bytes to short samples
                var samples = PcmBytesToShorts(audioData);
                _logger.LogDebug("Converted to {Count} samples", samples.Length);
                
                // Apply pre-emphasis filter
                samples = ApplyPreEmphasis(samples);
                _logger.LogDebug("Pre-emphasis applied ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);

                // Apply high-pass filter if enabled
                if (options.ApplyHighPassFilter)
                {
                    samples = ApplyHighPassFilter(samples, options.SampleRate, options.HighPassCutoff);
                    _logger.LogDebug("High-pass filter applied ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);
                }

                // Apply noise suppression using spectral subtraction (or noise gate for large buffers)
                samples = ApplySpectralSubtraction(samples, options.SampleRate, options.SuppressionStrength, _logger);
                _logger.LogDebug("Noise suppression applied ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);

                // Apply AGC if enabled
                if (options.ApplyAGC)
                {
                    samples = ApplyAutomaticGainControl(samples, options.TargetLevel);
                    _logger.LogDebug("AGC applied ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);
                }

                // Remove silence if enabled
                if (options.RemoveSilence)
                {
                    samples = RemoveSilentParts(samples, options.SilenceThreshold);
                    _logger.LogDebug("Silence removal applied ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);
                }

                // De-emphasis (restore natural sound)
                samples = ApplyDeEmphasis(samples);
                _logger.LogDebug("De-emphasis applied ({ElapsedMs}ms)", stopwatch.ElapsedMilliseconds);

                // Convert back to bytes
                var result = ShortsToPcmBytes(samples);
                stopwatch.Stop();
                _logger.LogInformation("Noise suppression complete: {InputBytes} → {OutputBytes} bytes in {ElapsedMs}ms", 
                    audioData.Length, result.Length, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error during noise suppression after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return audioData; // Return original on error
        }
    }

    public float EstimateNoiseLevel(byte[] audioData, int sampleRate = 16000)
    {
        if (audioData == null || audioData.Length < 2)
            return 0f;

        // Ensure even number of bytes
        if (audioData.Length % 2 != 0)
        {
            Array.Resize(ref audioData, audioData.Length - 1);
        }

        var samples = PcmBytesToShorts(audioData);
        
        // Calculate RMS energy
        double sumSquares = 0;
        foreach (var sample in samples)
        {
            sumSquares += sample * sample;
        }
        double rms = Math.Sqrt(sumSquares / samples.Length);
        
        // Normalize to 0-1 range (32768 is max short value)
        return (float)(rms / 32768.0);
    }

    public byte[] ApplyAGC(byte[] audioData, float targetLevel = 0.3f)
    {
        if (audioData == null || audioData.Length < 2)
            return audioData ?? Array.Empty<byte>();

        // Ensure even number of bytes
        if (audioData.Length % 2 != 0)
        {
            Array.Resize(ref audioData, audioData.Length - 1);
        }

        var samples = PcmBytesToShorts(audioData);
        samples = ApplyAutomaticGainControl(samples, targetLevel);
        return ShortsToPcmBytes(samples);
    }

    public byte[] RemoveEcho(byte[] audioData, byte[] referenceData, int sampleRate = 16000)
    {
        // Simple echo cancellation using adaptive filter (LMS algorithm)
        if (audioData == null || referenceData == null || audioData.Length < 2)
            return audioData ?? Array.Empty<byte>();

        // Ensure even number of bytes
        if (audioData.Length % 2 != 0)
        {
            Array.Resize(ref audioData, audioData.Length - 1);
        }
        if (referenceData.Length % 2 != 0)
        {
            Array.Resize(ref referenceData, referenceData.Length - 1);
        }

        try
        {
            var primary = PcmBytesToShorts(audioData);
            var reference = PcmBytesToShorts(referenceData);

            // Ensure reference is at least as long as primary
            if (reference.Length < primary.Length)
            {
                Array.Resize(ref reference, primary.Length);
            }

            var filterLength = Math.Min(128, primary.Length);
            var filterCoeffs = new float[filterLength];
            var output = new short[primary.Length];
            float mu = 0.001f; // Step size

            for (int n = 0; n < primary.Length; n++)
            {
                // Calculate filter output
                float filterOutput = 0;
                for (int k = 0; k < filterLength && n - k >= 0; k++)
                {
                    filterOutput += filterCoeffs[k] * reference[n - k];
                }

                // Error signal
                float error = primary[n] - filterOutput;
                output[n] = (short)Math.Clamp(error, short.MinValue, short.MaxValue);

                // Update filter coefficients (LMS)
                for (int k = 0; k < filterLength && n - k >= 0; k++)
                {
                    filterCoeffs[k] += mu * error * reference[n - k];
                }
            }

            return ShortsToPcmBytes(output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during echo cancellation");
            return audioData;
        }
    }

    #region Private Helper Methods

    private static short[] PcmBytesToShorts(byte[] bytes)
    {
        // Handle odd-length arrays by truncating to even length
        int length = bytes.Length / 2;
        var shorts = new short[length];
        for (int i = 0; i < length; i++)
        {
            shorts[i] = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
        }
        return shorts;
    }

    private static byte[] ShortsToPcmBytes(short[] shorts)
    {
        var bytes = new byte[shorts.Length * 2];
        for (int i = 0; i < shorts.Length; i++)
        {
            bytes[i * 2] = (byte)(shorts[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)((shorts[i] >> 8) & 0xFF);
        }
        return bytes;
    }

    private static short[] ApplyPreEmphasis(short[] samples)
    {
        var output = new short[samples.Length];
        output[0] = samples[0];
        
        for (int i = 1; i < samples.Length; i++)
        {
            float emphasized = samples[i] - PreEmphasisCoeff * samples[i - 1];
            output[i] = (short)Math.Clamp(emphasized, short.MinValue, short.MaxValue);
        }
        
        return output;
    }

    private static short[] ApplyDeEmphasis(short[] samples)
    {
        var output = new short[samples.Length];
        float previous = 0;
        
        for (int i = 0; i < samples.Length; i++)
        {
            float deemphasized = samples[i] + PreEmphasisCoeff * previous;
            output[i] = (short)Math.Clamp(deemphasized, short.MinValue, short.MaxValue);
            previous = output[i];
        }
        
        return output;
    }

    private static short[] ApplyHighPassFilter(short[] samples, int sampleRate, float cutoffFreq)
    {
        // Simple first-order high-pass filter
        float rc = 1.0f / (2.0f * MathF.PI * cutoffFreq);
        float dt = 1.0f / sampleRate;
        float alpha = rc / (rc + dt);

        var output = new short[samples.Length];
        float previousInput = 0;
        float previousOutput = 0;

        for (int i = 0; i < samples.Length; i++)
        {
            float input = samples[i];
            float filtered = alpha * (previousOutput + input - previousInput);
            output[i] = (short)Math.Clamp(filtered, short.MinValue, short.MaxValue);
            
            previousInput = input;
            previousOutput = filtered;
        }

        return output;
    }

    private static short[] ApplyAutomaticGainControl(short[] samples, float targetLevel)
    {
        // Calculate current RMS
        double sumSquares = 0;
        foreach (var sample in samples)
        {
            sumSquares += sample * sample;
        }
        double currentRms = Math.Sqrt(sumSquares / samples.Length);
        double targetRms = targetLevel * 32768.0;

        if (currentRms < 1) currentRms = 1; // Avoid division by zero

        // Calculate gain
        double gain = targetRms / currentRms;
        
        // Limit gain to prevent distortion
        gain = Math.Min(gain, 10.0);

        var output = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            double amplified = samples[i] * gain;
            output[i] = (short)Math.Clamp(amplified, short.MinValue, short.MaxValue);
        }

        return output;
    }

    private static short[] ApplySpectralSubtraction(short[] samples, int sampleRate, float suppressionStrength, ILogger? logger = null)
    {
        // For large audio files, skip spectral subtraction and use simpler noise gate
        // Spectral subtraction is too slow for real-time processing
        if (samples.Length > sampleRate * 2) // More than 2 seconds
        {
            logger?.LogDebug("Audio too long ({Length} samples), using simple noise gate instead of spectral subtraction", samples.Length);
            return ApplySimpleNoiseGate(samples, suppressionStrength);
        }

        // Estimate noise from first 200ms (assumed to be silence/noise)
        int noiseSamples = Math.Min(samples.Length, sampleRate / 5); // 200ms
        var noiseProfile = EstimateNoiseProfile(samples, noiseSamples);

        // Process in overlapping frames
        var output = new short[samples.Length];
        Array.Clear(output, 0, output.Length);
        
        var window = CreateHannWindow(FftSize);
        int totalFrames = (samples.Length - FftSize) / HopSize + 1;
        int processedFrames = 0;

        for (int i = 0; i < samples.Length - FftSize; i += HopSize)
        {
            // Extract frame
            var frame = new float[FftSize];
            for (int j = 0; j < FftSize; j++)
            {
                frame[j] = samples[i + j] * window[j];
            }

            // FFT
            var spectrum = FFT(frame);

            // Spectral subtraction
            for (int k = 0; k < spectrum.Length / 2; k++)
            {
                float magnitude = spectrum[k].Magnitude;
                float noise = noiseProfile[k] * suppressionStrength;
                float newMagnitude = Math.Max(magnitude - noise, 0);
                
                // Apply subtraction
                if (magnitude > 0)
                {
                    float scale = newMagnitude / magnitude;
                    spectrum[k] *= scale;
                    spectrum[spectrum.Length - 1 - k] *= scale; // Mirror for real signal
                }
            }

            // IFFT
            var processedFrame = IFFT(spectrum);

            // Overlap-add
            for (int j = 0; j < FftSize; j++)
            {
                if (i + j < output.Length)
                {
                    float value = processedFrame[j] * window[j];
                    output[i + j] = (short)Math.Clamp(output[i + j] + value, short.MinValue, short.MaxValue);
                }
            }
            
            processedFrames++;
        }

        return output;
    }
    
    private static short[] ApplySimpleNoiseGate(short[] samples, float threshold)
    {
        // Simple noise gate - faster than spectral subtraction for large buffers
        var output = new short[samples.Length];
        
        // Calculate RMS to determine noise floor
        double sumSquares = 0;
        foreach (var sample in samples)
        {
            sumSquares += sample * sample;
        }
        double rms = Math.Sqrt(sumSquares / samples.Length);
        double noiseFloor = rms * threshold * 0.5; // Conservative noise floor
        
        for (int i = 0; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) < noiseFloor)
            {
                output[i] = 0; // Silence below threshold
            }
            else
            {
                // Attenuate slightly
                output[i] = (short)(samples[i] * 0.9);
            }
        }
        
        return output;
    }

    private static float[] EstimateNoiseProfile(short[] samples, int noiseSamples)
    {
        var noiseProfile = new float[FftSize / 2];
        
        // Analyze first few frames as noise
        int frameCount = 0;
        for (int i = 0; i < noiseSamples - FftSize; i += HopSize)
        {
            var frame = new float[FftSize];
            for (int j = 0; j < FftSize; j++)
            {
                frame[j] = samples[i + j];
            }

            var spectrum = FFT(frame);
            for (int k = 0; k < noiseProfile.Length; k++)
            {
                noiseProfile[k] += spectrum[k].Magnitude;
            }
            frameCount++;
        }

        // Average
        if (frameCount > 0)
        {
            for (int k = 0; k < noiseProfile.Length; k++)
            {
                noiseProfile[k] /= frameCount;
            }
        }

        return noiseProfile;
    }

    private static float[] CreateHannWindow(int size)
    {
        var window = new float[size];
        for (int i = 0; i < size; i++)
        {
            window[i] = 0.5f * (1 - MathF.Cos(2 * MathF.PI * i / (size - 1)));
        }
        return window;
    }

    private static Complex[] FFT(float[] input)
    {
        int n = input.Length;
        var output = new Complex[n];
        
        // Copy input
        for (int i = 0; i < n; i++)
        {
            output[i] = new Complex(input[i], 0);
        }

        // Cooley-Tukey FFT
        FFTRecursive(output, n);

        return output;
    }

    private static float[] IFFT(Complex[] input)
    {
        int n = input.Length;
        var spectrum = new Complex[n];
        Array.Copy(input, spectrum, n);

        // Conjugate
        for (int i = 0; i < n; i++)
        {
            spectrum[i] = new Complex(spectrum[i].Real, -spectrum[i].Imaginary);
        }

        // FFT
        FFTRecursive(spectrum, n);

        // Conjugate and scale
        var output = new float[n];
        for (int i = 0; i < n; i++)
        {
            output[i] = (float)(spectrum[i].Real / n);
        }

        return output;
    }

    private static void FFTRecursive(Complex[] data, int n)
    {
        if (n <= 1) return;

        // Bit-reverse permutation
        int j = 0;
        for (int i = 0; i < n; i++)
        {
            if (i < j)
            {
                var temp = data[i];
                data[i] = data[j];
                data[j] = temp;
            }
            int bit = n >> 1;
            while (j >= bit)
            {
                j -= bit;
                bit >>= 1;
            }
            j += bit;
        }

        // Danielson-Lanczos section
        for (int len = 2; len <= n; len <<= 1)
        {
            float angle = -2 * MathF.PI / len;
            var wlen = new Complex(MathF.Cos(angle), MathF.Sin(angle));
            
            for (int i = 0; i < n; i += len)
            {
                var w = new Complex(1, 0);
                for (int k = 0; k < len / 2; k++)
                {
                    var u = data[i + k];
                    var v = data[i + k + len / 2] * w;
                    data[i + k] = u + v;
                    data[i + k + len / 2] = u - v;
                    w *= wlen;
                }
            }
        }
    }

    private static short[] RemoveSilentParts(short[] samples, float threshold)
    {
        // Simple implementation - just return original
        // A full implementation would trim leading/trailing silence
        return samples;
    }

    private readonly struct Complex
    {
        public readonly float Real;
        public readonly float Imaginary;

        public Complex(float real, float imaginary)
        {
            Real = real;
            Imaginary = imaginary;
        }

        public float Magnitude => MathF.Sqrt(Real * Real + Imaginary * Imaginary);

        public static Complex operator +(Complex a, Complex b) => 
            new Complex(a.Real + b.Real, a.Imaginary + b.Imaginary);
        
        public static Complex operator -(Complex a, Complex b) => 
            new Complex(a.Real - b.Real, a.Imaginary - b.Imaginary);
        
        public static Complex operator *(Complex a, Complex b) => 
            new Complex(
                a.Real * b.Real - a.Imaginary * b.Imaginary,
                a.Real * b.Imaginary + a.Imaginary * b.Real);

        public static Complex operator *(Complex a, float scalar) => 
            new Complex(a.Real * scalar, a.Imaginary * scalar);
    }

    #endregion
}
