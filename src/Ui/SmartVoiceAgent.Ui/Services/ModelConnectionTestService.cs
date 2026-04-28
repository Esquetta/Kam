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

            return liveModelCount > 0
                ? ModelConnectionTestResult.Passed(liveModelCount)
                : ModelConnectionTestResult.Failed("Provider did not return live available models.");
        }
        catch (Exception ex)
        {
            return ModelConnectionTestResult.Failed(ex.Message);
        }
    }

    public void Dispose()
    {
        if (_ownsLiveCatalog && _liveCatalog is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
