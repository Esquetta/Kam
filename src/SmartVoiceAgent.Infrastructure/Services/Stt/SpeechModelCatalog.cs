using Whisper.net.Ggml;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// A local Whisper model Kam can download.
/// </summary>
/// <param name="Name">The name Settings store, such as <c>base</c>.</param>
/// <param name="Type">The Whisper model size.</param>
/// <param name="Quantization">The weight format.</param>
/// <param name="FileName">The file the model is kept in.</param>
/// <param name="ApproximateBytes">The download size, for progress.</param>
public sealed record SpeechModelInfo(
    string Name,
    GgmlType Type,
    QuantizationType Quantization,
    string FileName,
    long ApproximateBytes);

/// <summary>
/// The local Whisper models Kam offers. <c>tiny</c> listens for the wake word; the others transcribe commands.
/// </summary>
public static class SpeechModelCatalog
{
    /// <summary>
    /// The model used when none is chosen.
    /// </summary>
    public const string DefaultModel = "base";

    /// <summary>
    /// The small model that listens for the wake word.
    /// </summary>
    public const string WakeWordModel = "tiny";

    /// <summary>
    /// Gets every model by name.
    /// </summary>
    public static IReadOnlyList<SpeechModelInfo> All { get; } =
    [
        new("tiny", GgmlType.Tiny, QuantizationType.NoQuantization, "ggml-tiny.bin", 77_691_713),
        new("base", GgmlType.Base, QuantizationType.NoQuantization, "ggml-base.bin", 147_951_465),
        new("small", GgmlType.Small, QuantizationType.NoQuantization, "ggml-small.bin", 487_601_967),
        new("large-v3-turbo", GgmlType.LargeV3Turbo, QuantizationType.Q5_0, "ggml-large-v3-turbo-q5_0.bin", 574_041_195)
    ];

    /// <summary>
    /// Returns a known model name, or <see cref="DefaultModel"/>.
    /// </summary>
    /// <param name="model">The saved model name.</param>
    public static string Normalize(string? model)
    {
        var match = All.FirstOrDefault(info => info.Name.Equals(model?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Name ?? DefaultModel;
    }

    /// <summary>
    /// Returns the model with <paramref name="model"/>'s name, or the default model.
    /// </summary>
    /// <param name="model">The model name.</param>
    public static SpeechModelInfo Get(string? model)
    {
        var name = Normalize(model);
        return All.First(info => info.Name == name);
    }
}
