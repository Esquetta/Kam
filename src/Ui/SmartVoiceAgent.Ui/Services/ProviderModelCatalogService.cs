using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public sealed class ProviderModelCatalogService : IModelCatalogService, IDisposable
{
    private readonly IModelCatalogService _openAiCompatibleCatalog;
    private readonly IModelCatalogService _anthropicCatalog;
    private readonly bool _ownsCatalogs;

    public ProviderModelCatalogService()
        : this(
            new OpenAiCompatibleModelCatalogService(),
            new AnthropicModelCatalogService(),
            ownsCatalogs: true)
    {
    }

    public ProviderModelCatalogService(
        IModelCatalogService openAiCompatibleCatalog,
        IModelCatalogService anthropicCatalog)
        : this(openAiCompatibleCatalog, anthropicCatalog, ownsCatalogs: false)
    {
    }

    private ProviderModelCatalogService(
        IModelCatalogService openAiCompatibleCatalog,
        IModelCatalogService anthropicCatalog,
        bool ownsCatalogs)
    {
        _openAiCompatibleCatalog = openAiCompatibleCatalog;
        _anthropicCatalog = anthropicCatalog;
        _ownsCatalogs = ownsCatalogs;
    }

    public Task<IReadOnlyList<ModelCatalogEntry>> GetModelsAsync(
        ModelProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Provider == ModelProviderType.Anthropic
            ? _anthropicCatalog.GetModelsAsync(profile, cancellationToken)
            : _openAiCompatibleCatalog.GetModelsAsync(profile, cancellationToken);
    }

    public void Dispose()
    {
        if (!_ownsCatalogs)
        {
            return;
        }

        if (_openAiCompatibleCatalog is IDisposable openAiDisposable)
        {
            openAiDisposable.Dispose();
        }

        if (_anthropicCatalog is IDisposable anthropicDisposable)
        {
            anthropicDisposable.Dispose();
        }
    }
}
