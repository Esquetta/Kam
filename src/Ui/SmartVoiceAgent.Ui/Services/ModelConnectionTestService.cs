using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public sealed class ModelConnectionTestService : IModelConnectionTestService, IDisposable
{
    private readonly IModelCatalogService _liveCatalog;
    private readonly bool _ownsLiveCatalog;

    public ModelConnectionTestService()
        : this(new OpenAiCompatibleModelCatalogService(), ownsLiveCatalog: true)
    {
    }

    public ModelConnectionTestService(IModelCatalogService liveCatalog)
        : this(liveCatalog, ownsLiveCatalog: false)
    {
    }

    private ModelConnectionTestService(IModelCatalogService liveCatalog, bool ownsLiveCatalog)
    {
        _liveCatalog = liveCatalog;
        _ownsLiveCatalog = ownsLiveCatalog;
    }

    public async Task<ModelConnectionTestResult> TestAsync(
        ModelProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        try
        {
            var models = await _liveCatalog.GetModelsAsync(profile, cancellationToken).ConfigureAwait(false);
            var liveModelCount = models.Count(model => model.IsAvailable);
            var testedAt = DateTimeOffset.UtcNow;

            return liveModelCount > 0
                ? ModelConnectionTestResult.Passed(profile.Provider, profile.ModelId, liveModelCount, testedAt)
                : ModelConnectionTestResult.Failed(
                    profile.Provider,
                    profile.ModelId,
                    "Provider did not return live available models.",
                    "CatalogEmpty",
                    testedAt);
        }
        catch (Exception ex)
        {
            return ModelConnectionTestResult.Failed(
                profile.Provider,
                profile.ModelId,
                SanitizeMessage(ex.Message, profile),
                CategorizeFailure(ex),
                DateTimeOffset.UtcNow);
        }
    }

    public void Dispose()
    {
        if (_ownsLiveCatalog && _liveCatalog is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string CategorizeFailure(Exception exception)
    {
        var message = exception.Message;
        if (message.Contains("401", StringComparison.OrdinalIgnoreCase)
            || message.Contains("403", StringComparison.OrdinalIgnoreCase)
            || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
        {
            return "Authentication";
        }

        if (exception is OperationCanceledException)
        {
            return "Timeout";
        }

        if (message.Contains("endpoint", StringComparison.OrdinalIgnoreCase)
            || message.Contains("uri", StringComparison.OrdinalIgnoreCase))
        {
            return "Configuration";
        }

        return "Connection";
    }

    private static string SanitizeMessage(string message, ModelProviderProfile profile)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Provider connection failed.";
        }

        var sanitized = message;
        sanitized = ReplaceIfPresent(sanitized, profile.ApiKey);
        sanitized = ReplaceIfPresent(sanitized, profile.Endpoint);
        return sanitized;
    }

    private static string ReplaceIfPresent(string value, string secret)
    {
        return string.IsNullOrWhiteSpace(secret)
            ? value
            : value.Replace(secret, "[redacted]", StringComparison.OrdinalIgnoreCase);
    }
}
