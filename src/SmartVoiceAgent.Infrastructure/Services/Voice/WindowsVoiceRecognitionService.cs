using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SmartVoiceAgent.Infrastructure.Services.Voice;

/// <summary>
/// Records on Windows: the microphone chosen in Settings through WASAPI, converted to 16 kHz mono, or the
/// default microphone through WaveIn when none is chosen or the chosen one can't be opened.
/// </summary>
public sealed class WindowsVoiceRecognitionService : VoiceRecognitionServiceBase
{
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly string? _deviceId;
    private IWaveIn? _capture;
    private MMDevice? _device;
    private PcmConverter? _converter;

    /// <summary>
    /// Creates a recorder.
    /// </summary>
    /// <param name="deviceId">The WASAPI endpoint id of the microphone, or null for the default one.</param>
    public WindowsVoiceRecognitionService(string? deviceId = null)
    {
        _deviceId = deviceId;
    }

    /// <inheritdoc />
    protected override void StartListeningInternal()
    {
        _capture = (IWaveIn?)TryCreateWasapiCapture() ?? new WaveInEvent
        {
            WaveFormat = new WaveFormat(WaveAudio.SampleRate, 16, 1),
            BufferMilliseconds = 50
        };

        var format = _capture.WaveFormat;
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat);
        _converter = new PcmConverter(format.SampleRate, format.Channels, format.BitsPerSample, isFloat, WaveAudio.SampleRate);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
    }

    /// <inheritdoc />
    protected override void StopListeningInternal()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.StopRecording();
        }
    }

    /// <inheritdoc />
    protected override void CleanupPlatformResources()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }

        _device?.Dispose();
        _device = null;
        _converter = null;
    }

    private WasapiCapture? TryCreateWasapiCapture()
    {
        if (string.IsNullOrWhiteSpace(_deviceId))
        {
            return null;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(_deviceId);
            if (device.State != DeviceState.Active)
            {
                device.Dispose();
                return null;
            }

            _device = device;
            return new WasapiCapture(device, true, 50);
        }
        catch (Exception ex) when (ex is global::System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            InvokeOnError(new InvalidOperationException($"The chosen microphone can't be opened; using the default one. {ex.Message}", ex));
            _device?.Dispose();
            _device = null;
            return null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            var converter = _converter;
            if (converter is null || e.BytesRecorded <= 0)
            {
                return;
            }

            AddAudioData(converter.Convert(e.Buffer.AsSpan(0, Math.Min(e.BytesRecorded, e.Buffer.Length))));
        }
        catch (Exception ex)
        {
            InvokeOnError(ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            // The device went away or failed while recording.
            ThreadPool.QueueUserWorkItem(_ => ReportError(e.Exception));
        }
    }
}
