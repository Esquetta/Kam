using NAudio.CoreAudioApi;
using NAudio.Wave;
using SmartVoiceAgent.Core.Interfaces;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SmartVoiceAgent.Infrastructure.Services.Speech;

/// <summary>
/// Speaks with the Windows speech API. Lists the newer OneCore voices as well as the classic ones, so voices
/// such as Turkish "Tolga" are available. Audio is synthesized to memory and played with NAudio on the speaker
/// chosen in Settings.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSpeechSynthesizer : ISpeechSynthesizer
{
    private const string OneCoreVoices = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices";
    private const string ClassicVoices = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices";
    private const int Format22kHz16BitMono = 22;
    private const int SpeakIsNotXml = 16;
    private static readonly WaveFormat OutputFormat = new(22050, 16, 1);

    private IReadOnlyList<SpeechVoiceInfo>? _voices;

    /// <inheritdoc />
    public bool IsAvailable => Type.GetTypeFromProgID("SAPI.SpVoice") is not null;

    /// <inheritdoc />
    public IReadOnlyList<SpeechVoiceInfo> GetVoices()
    {
        if (_voices is not null)
        {
            return _voices;
        }

        var voices = new List<SpeechVoiceInfo>();
        foreach (var categoryId in new[] { OneCoreVoices, ClassicVoices })
        {
            object? category = null;
            object? tokens = null;
            try
            {
                category = Create("SAPI.SpObjectTokenCategory");
                Call(category, "SetId", categoryId, false);
                tokens = Call(category, "EnumerateTokens")!;
                var count = (int)Get(tokens, "Count")!;
                for (var i = 0; i < count; i++)
                {
                    var token = Call(tokens, "Item", i)!;
                    try
                    {
                        var id = (string)Get(token, "Id")!;
                        var name = (string)Call(token, "GetDescription", 0)!;
                        var language = LanguageOf(Call(token, "GetAttribute", "Language") as string);
                        if (!voices.Any(voice => voice.Name == name))
                        {
                            voices.Add(new SpeechVoiceInfo(id, name, language));
                        }
                    }
                    finally
                    {
                        Release(token);
                    }
                }
            }
            catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidCastException)
            {
                // This category isn't on this Windows version.
            }
            finally
            {
                Release(tokens);
                Release(category);
            }
        }

        _voices = voices;
        return voices;
    }

    /// <inheritdoc />
    public async Task SpeakAsync(
        string text,
        string? voiceId,
        string language,
        int rate,
        string? outputDeviceId,
        CancellationToken cancellationToken)
    {
        var audio = await Task.Run(() => Synthesize(text, voiceId, rate), cancellationToken).ConfigureAwait(false);
        if (audio.Length == 0)
        {
            return;
        }

        await PlayAsync(audio, outputDeviceId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the two-letter language for a SAPI language attribute such as <c>41F</c> or <c>409;9</c>.
    /// </summary>
    /// <param name="attribute">The attribute value.</param>
    public static string LanguageOf(string? attribute)
    {
        var first = attribute?.Split(';')[0].Trim();
        if (string.IsNullOrEmpty(first)
            || !int.TryParse(first, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid))
        {
            return string.Empty;
        }

        try
        {
            return CultureInfo.GetCultureInfo(lcid).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return string.Empty;
        }
    }

    private static byte[] Synthesize(string text, string? voiceId, int rate)
    {
        object? voice = null;
        object? stream = null;
        object? format = null;
        object? token = null;
        try
        {
            voice = Create("SAPI.SpVoice");
            stream = Create("SAPI.SpMemoryStream");
            format = Get(stream, "Format")!;
            Set(format, "Type", Format22kHz16BitMono);
            SetRef(stream, "Format", format);

            if (!string.IsNullOrEmpty(voiceId))
            {
                token = Create("SAPI.SpObjectToken");
                Call(token, "SetId", voiceId, string.Empty, false);
                SetRef(voice, "Voice", token);
            }

            Set(voice, "Rate", Math.Clamp(rate * 2, -10, 10));
            SetRef(voice, "AudioOutputStream", stream);
            Call(voice, "Speak", text, SpeakIsNotXml);
            return Call(stream, "GetData") as byte[] ?? [];
        }
        finally
        {
            Release(token);
            Release(format);
            Release(stream);
            Release(voice);
        }
    }

    private static async Task PlayAsync(byte[] audio, string? outputDeviceId, CancellationToken cancellationToken)
    {
        using var source = new RawSourceWaveStream(new MemoryStream(audio), OutputFormat);
        using var device = OpenDevice(outputDeviceId);
        using IWavePlayer player = device is null ? new WaveOutEvent() : new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackStopped += (_, _) => finished.TrySetResult();
        player.Init(source);
        player.Play();
        using (cancellationToken.Register(() => player.Stop()))
        {
            await finished.Task.ConfigureAwait(false);
        }
    }

    private static MMDevice? OpenDevice(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(deviceId);
            if (device.State == DeviceState.Active)
            {
                return device;
            }

            device.Dispose();
        }
        catch (COMException)
        {
        }

        return null;
    }

    private static object Create(string progId)
    {
        var type = Type.GetTypeFromProgID(progId) ?? throw new COMException($"{progId} is not registered.");
        return Activator.CreateInstance(type) ?? throw new COMException($"{progId} could not be created.");
    }

    private static object? Call(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args, CultureInfo.InvariantCulture);

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null, CultureInfo.InvariantCulture);

    private static void Set(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value], CultureInfo.InvariantCulture);

    private static void SetRef(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.PutRefDispProperty, null, target, [value], CultureInfo.InvariantCulture);

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
