namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// Tunes <see cref="VoiceActivityDetector"/>. Levels are RMS of 16-bit samples scaled to 0..1.
/// </summary>
public sealed record VoiceActivityOptions
{
    /// <summary>
    /// Gets the sample rate of the audio, in hertz.
    /// </summary>
    public int SampleRate { get; init; } = 16000;

    /// <summary>
    /// Gets the lowest level that counts as speech, however quiet the room.
    /// </summary>
    public double MinThreshold { get; init; } = 0.006;

    /// <summary>
    /// Gets how far above the background noise speech must be.
    /// </summary>
    public double NoiseMultiplier { get; init; } = 3.0;

    /// <summary>
    /// Gets the share of the threshold speech may drop to before it counts as silence.
    /// </summary>
    public double ReleaseRatio { get; init; } = 0.6;

    /// <summary>
    /// Gets how long sound must stay above the threshold to start an utterance.
    /// </summary>
    public int StartMs { get; init; } = 100;

    /// <summary>
    /// Gets how long silence must last to end an utterance.
    /// </summary>
    public int SilenceMs { get; init; } = 800;

    /// <summary>
    /// Gets how much audio before the start is kept, so the first syllable isn't cut.
    /// </summary>
    public int PreRollMs { get; init; } = 300;

    /// <summary>
    /// Gets the least speech an utterance needs; shorter sounds are dropped as clicks.
    /// </summary>
    public int MinSpeechMs { get; init; } = 250;

    /// <summary>
    /// Gets the longest utterance; longer speech is cut and returned at this length.
    /// </summary>
    public int MaxUtteranceMs { get; init; } = 30000;
}

/// <summary>
/// Finds utterances in 16 kHz mono 16-bit PCM: speech that starts above an adaptive noise threshold and ends
/// after a stretch of silence. Not thread-safe; feed it from one thread or under a lock.
/// </summary>
public sealed class VoiceActivityDetector
{
    private const int FrameMs = 20;

    private readonly VoiceActivityOptions _options;
    private readonly int _frameBytes;
    private readonly byte[] _pending;
    private readonly Queue<byte[]> _preRoll = new();
    private readonly List<byte[]> _candidate = [];
    private readonly MemoryStream _utterance = new();
    private int _pendingCount;
    private double _noiseFloor;
    private int _voicedMs;
    private int _silenceMs;

    /// <summary>
    /// Creates a detector.
    /// </summary>
    /// <param name="options">The tuning, or the defaults.</param>
    public VoiceActivityDetector(VoiceActivityOptions? options = null)
    {
        _options = options ?? new VoiceActivityOptions();
        _frameBytes = _options.SampleRate * FrameMs / 1000 * 2;
        _pending = new byte[_frameBytes];
        Reset();
    }

    /// <summary>
    /// Gets whether an utterance is in progress.
    /// </summary>
    public bool InSpeech { get; private set; }

    /// <summary>
    /// Gets the level of the last frame, 0..1.
    /// </summary>
    public double Level { get; private set; }

    /// <summary>
    /// Gets the highest level since the last reset, 0..1.
    /// </summary>
    public double PeakLevel { get; private set; }

    /// <summary>
    /// Gets the estimated background noise level, 0..1.
    /// </summary>
    public double NoiseFloor => _noiseFloor;

    /// <summary>
    /// Gets the level that currently counts as speech.
    /// </summary>
    public double Threshold => Math.Max(_options.MinThreshold, _noiseFloor * _options.NoiseMultiplier);

    /// <summary>
    /// Feeds audio and returns the utterances it completed, usually none.
    /// </summary>
    /// <param name="pcm">16-bit little-endian mono samples at <see cref="VoiceActivityOptions.SampleRate"/>.</param>
    public IReadOnlyList<byte[]> Process(ReadOnlySpan<byte> pcm)
    {
        List<byte[]>? completed = null;
        while (!pcm.IsEmpty)
        {
            var take = Math.Min(_frameBytes - _pendingCount, pcm.Length);
            pcm[..take].CopyTo(_pending.AsSpan(_pendingCount));
            _pendingCount += take;
            pcm = pcm[take..];

            if (_pendingCount == _frameBytes)
            {
                _pendingCount = 0;
                var utterance = ProcessFrame(_pending.ToArray());
                if (utterance is not null)
                {
                    (completed ??= []).Add(utterance);
                }
            }
        }

        return completed ?? (IReadOnlyList<byte[]>)[];
    }

    /// <summary>
    /// Ends the utterance in progress, if any, and returns it when it holds enough speech.
    /// </summary>
    public byte[]? Flush()
    {
        if (!InSpeech)
        {
            return null;
        }

        return EndUtterance();
    }

    /// <summary>
    /// Forgets all audio and levels.
    /// </summary>
    public void Reset()
    {
        _pendingCount = 0;
        _preRoll.Clear();
        _candidate.Clear();
        _utterance.SetLength(0);
        _noiseFloor = _options.MinThreshold / _options.NoiseMultiplier / 2;
        _voicedMs = 0;
        _silenceMs = 0;
        InSpeech = false;
        Level = 0;
        PeakLevel = 0;
    }

    /// <summary>
    /// Returns the RMS level of 16-bit little-endian samples, 0..1.
    /// </summary>
    /// <param name="pcm">The samples.</param>
    public static double Rms(ReadOnlySpan<byte> pcm)
    {
        var count = pcm.Length / 2;
        if (count == 0)
        {
            return 0;
        }

        double sum = 0;
        for (var i = 0; i < count; i++)
        {
            var sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768.0;
            sum += sample * sample;
        }

        return Math.Sqrt(sum / count);
    }

    private byte[]? ProcessFrame(byte[] frame)
    {
        var level = Rms(frame);
        Level = level;
        PeakLevel = Math.Max(PeakLevel, level);
        var threshold = Threshold;

        if (InSpeech)
        {
            _utterance.Write(frame);
            if (level >= threshold * _options.ReleaseRatio)
            {
                _voicedMs += FrameMs;
                _silenceMs = 0;
            }
            else
            {
                _silenceMs += FrameMs;
            }

            if (_silenceMs >= _options.SilenceMs || _utterance.Length >= BytesFor(_options.MaxUtteranceMs))
            {
                return EndUtterance();
            }

            return null;
        }

        if (level >= threshold)
        {
            _candidate.Add(frame);
            if (_candidate.Count * FrameMs >= _options.StartMs)
            {
                StartUtterance();
            }

            return null;
        }

        // Quiet frame: it becomes part of the noise estimate and the pre-roll.
        foreach (var pending in _candidate)
        {
            AddPreRoll(pending);
        }

        _candidate.Clear();
        AddPreRoll(frame);
        UpdateNoiseFloor(level);
        return null;
    }

    private void StartUtterance()
    {
        InSpeech = true;
        _utterance.SetLength(0);
        foreach (var frame in _preRoll)
        {
            _utterance.Write(frame);
        }

        foreach (var frame in _candidate)
        {
            _utterance.Write(frame);
        }

        _voicedMs = _candidate.Count * FrameMs;
        _silenceMs = 0;
        _preRoll.Clear();
        _candidate.Clear();
    }

    private byte[]? EndUtterance()
    {
        var enough = _voicedMs >= _options.MinSpeechMs;
        var audio = enough ? _utterance.ToArray() : null;
        _utterance.SetLength(0);
        _voicedMs = 0;
        _silenceMs = 0;
        InSpeech = false;
        return audio;
    }

    private void AddPreRoll(byte[] frame)
    {
        _preRoll.Enqueue(frame);
        while (_preRoll.Count * FrameMs > _options.PreRollMs)
        {
            _preRoll.Dequeue();
        }
    }

    private void UpdateNoiseFloor(double level)
    {
        // Falls quickly toward a quieter room and rises slowly, so speech doesn't raise it.
        _noiseFloor = level < _noiseFloor
            ? _noiseFloor * 0.7 + level * 0.3
            : _noiseFloor * 0.98 + level * 0.02;
        _noiseFloor = Math.Clamp(_noiseFloor, 0.0002, 0.05);
    }

    private long BytesFor(int milliseconds) => (long)_options.SampleRate * milliseconds / 1000 * 2;
}
