using SmartVoiceAgent.Core.Interfaces;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services.Speech;

/// <summary>
/// Speaks with a command-line engine: <c>say</c> on macOS, <c>spd-say</c> or <c>espeak-ng</c> on Linux.
/// </summary>
public sealed partial class CommandLineSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly string _engine;
    private IReadOnlyList<SpeechVoiceInfo>? _voices;

    private CommandLineSpeechSynthesizer(string engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Returns a synthesizer for the first engine found on the path, or null when there is none.
    /// </summary>
    public static CommandLineSpeechSynthesizer? TryCreate()
    {
        var candidates = OperatingSystem.IsMacOS() ? new[] { "say" } : ["spd-say", "espeak-ng", "espeak"];
        var engine = candidates.FirstOrDefault(IsOnPath);
        return engine is null ? null : new CommandLineSpeechSynthesizer(engine);
    }

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public IReadOnlyList<SpeechVoiceInfo> GetVoices()
    {
        if (_voices is not null)
        {
            return _voices;
        }

        var voices = new List<SpeechVoiceInfo>();
        if (_engine == "say")
        {
            foreach (var line in Run("say", "-v", "?").Split('\n'))
            {
                var match = SayVoice().Match(line);
                if (match.Success)
                {
                    var name = match.Groups["name"].Value.Trim();
                    voices.Add(new SpeechVoiceInfo(name, name, match.Groups["lang"].Value));
                }
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
        var startInfo = new ProcessStartInfo(_engine)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        switch (_engine)
        {
            case "say":
                if (!string.IsNullOrEmpty(voiceId))
                {
                    startInfo.ArgumentList.Add("-v");
                    startInfo.ArgumentList.Add(voiceId);
                }

                startInfo.ArgumentList.Add("-r");
                startInfo.ArgumentList.Add((175 + rate * 25).ToString(CultureInfo.InvariantCulture));
                break;
            case "spd-say":
                startInfo.ArgumentList.Add("-w");
                startInfo.ArgumentList.Add("-l");
                startInfo.ArgumentList.Add(language);
                startInfo.ArgumentList.Add("-r");
                startInfo.ArgumentList.Add((rate * 20).ToString(CultureInfo.InvariantCulture));
                break;
            default:
                startInfo.ArgumentList.Add("-v");
                startInfo.ArgumentList.Add(language);
                startInfo.ArgumentList.Add("-s");
                startInfo.ArgumentList.Add((175 + rate * 25).ToString(CultureInfo.InvariantCulture));
                break;
        }

        // Text that starts with a dash would read as an option.
        startInfo.ArgumentList.Add(text.TrimStart('-', ' '));

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill();
            }

            throw;
        }
    }

    private static bool IsOnPath(string program)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, program)));
    }

    private static string Run(string program, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(program) { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            return output;
        }
        catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    [GeneratedRegex(@"^(?<name>.+?)\s{2,}(?<lang>[a-z]{2})[_-][A-Za-z]{2}\s+#")]
    private static partial Regex SayVoice();
}
