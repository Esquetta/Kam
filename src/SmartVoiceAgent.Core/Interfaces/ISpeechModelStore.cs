namespace SmartVoiceAgent.Core.Interfaces;

/// <summary>
/// Keeps the local speech recognition models and downloads them on demand.
/// </summary>
public interface ISpeechModelStore
{
    /// <summary>
    /// Gets the folder the models are kept in.
    /// </summary>
    string ModelsDirectory { get; }

    /// <summary>
    /// Returns the file a model is kept in, whether or not it was downloaded.
    /// </summary>
    /// <param name="model">The model name, such as <c>base</c>.</param>
    string GetModelPath(string model);

    /// <summary>
    /// Returns whether a model is on this computer.
    /// </summary>
    /// <param name="model">The model name.</param>
    bool IsDownloaded(string model);

    /// <summary>
    /// Returns the model file, downloading it first when it is missing.
    /// </summary>
    /// <param name="model">The model name.</param>
    /// <param name="progress">Receives the download progress from 0 to 1.</param>
    /// <param name="cancellationToken">Stops the download.</param>
    Task<string> EnsureModelAsync(string model, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
