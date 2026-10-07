using Microsoft.Extensions.Logging;
using SmartVoiceAgent.Core.Interfaces;
using Whisper.net.Ggml;

namespace SmartVoiceAgent.Infrastructure.Services;

/// <summary>
/// Keeps Whisper models in <c>%LocalAppData%/Kam/Models</c> and downloads them from the Whisper.net model
/// repository the first time they are needed.
/// </summary>
public sealed class WhisperModelStore : ISpeechModelStore, IDisposable
{
    private readonly ILogger<WhisperModelStore> _logger;
    private readonly Func<SpeechModelInfo, CancellationToken, Task<Stream>> _download;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    /// <summary>
    /// Creates a store in the default folder.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public WhisperModelStore(ILogger<WhisperModelStore> logger)
        : this(logger, DefaultDirectory(), DownloadAsync)
    {
    }

    /// <summary>
    /// Creates a store in <paramref name="modelsDirectory"/> that downloads with <paramref name="download"/>.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="modelsDirectory">The folder models are kept in.</param>
    /// <param name="download">Opens a model's download stream.</param>
    public WhisperModelStore(
        ILogger<WhisperModelStore> logger,
        string modelsDirectory,
        Func<SpeechModelInfo, CancellationToken, Task<Stream>> download)
    {
        _logger = logger;
        ModelsDirectory = modelsDirectory;
        _download = download;
    }

    /// <inheritdoc />
    public string ModelsDirectory { get; }

    /// <inheritdoc />
    public string GetModelPath(string model) => Path.Combine(ModelsDirectory, SpeechModelCatalog.Get(model).FileName);

    /// <inheritdoc />
    public bool IsDownloaded(string model) => File.Exists(GetModelPath(model));

    /// <inheritdoc />
    public async Task<string> EnsureModelAsync(
        string model,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var info = SpeechModelCatalog.Get(model);
        var path = GetModelPath(info.Name);
        if (File.Exists(path))
        {
            return path;
        }

        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path))
            {
                return path;
            }

            Directory.CreateDirectory(ModelsDirectory);
            var partial = path + ".download";
            _logger.LogInformation("Downloading speech model {Model} to {Path}", info.Name, path);
            try
            {
                await using (var source = await _download(info, cancellationToken).ConfigureAwait(false))
                await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    var buffer = new byte[81920];
                    long copied = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        copied += read;
                        progress?.Report(Math.Min(0.99, copied / (double)info.ApproximateBytes));
                    }
                }

                File.Move(partial, path, overwrite: true);
            }
            catch
            {
                TryDelete(partial);
                throw;
            }

            progress?.Report(1);
            _logger.LogInformation("Speech model {Model} downloaded", info.Name);
            return path;
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _downloadGate.Dispose();

    private static string DefaultDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kam",
            "Models");
    }

    private static Task<Stream> DownloadAsync(SpeechModelInfo info, CancellationToken cancellationToken)
    {
        return WhisperGgmlDownloader.Default.GetGgmlModelAsync(info.Type, info.Quantization, cancellationToken);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
