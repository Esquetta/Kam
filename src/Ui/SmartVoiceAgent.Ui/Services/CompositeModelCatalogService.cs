using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

public sealed class CompositeModelCatalogService : IModelCatalogService, IDisposable
{
    private readonly IModelCatalogService _liveCatalog;
    private readonly IModelCatalogService _metadataCatalog;
    private readonly bool _ownsCatalogs;

    public CompositeModelCatalogService(
        IModelCatalogService liveCatalog,
        IModelCatalogService metadataCatalog)
        : this(liveCatalog, metadataCatalog, ownsCatalogs: false)
    {
    }

    private CompositeModelCatalogService(
        IModelCatalogService liveCatalog,
        IModelCatalogService metadataCatalog,
        bool ownsCatalogs)
    {
        _liveCatalog = liveCatalog;
        _metadataCatalog = metadataCatalog;
        _ownsCatalogs = ownsCatalogs;
    }

    public static CompositeModelCatalogService CreateDefault()
    {
        return new CompositeModelCatalogService(
            new OpenAiCompatibleModelCatalogService(),
            new ModelsDevModelCatalogService(),
            ownsCatalogs: true);
    }

    public async Task<IReadOnlyList<ModelCatalogEntry>> GetModelsAsync(
        ModelProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var metadataModels = await TryGetModelsAsync(_metadataCatalog, profile, cancellationToken).ConfigureAwait(false);
        var liveModels = await TryGetModelsAsync(_liveCatalog, profile, cancellationToken).ConfigureAwait(false);

        if (liveModels.Count == 0)
        {
            return metadataModels;
        }

        var metadataById = metadataModels
            .GroupBy(model => NormalizeModelId(model.ModelId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return liveModels
            .Select(model => metadataById.TryGetValue(NormalizeModelId(model.ModelId), out var metadata)
                ? Enrich(model, metadata)
                : model)
            .OrderByDescending(model => model.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Dispose()
    {
        if (!_ownsCatalogs)
        {
            return;
        }

        if (_liveCatalog is IDisposable liveDisposable)
        {
            liveDisposable.Dispose();
        }

        if (_metadataCatalog is IDisposable metadataDisposable)
        {
            metadataDisposable.Dispose();
        }
    }

    private static async Task<IReadOnlyList<ModelCatalogEntry>> TryGetModelsAsync(
        IModelCatalogService catalog,
        ModelProviderProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            return await catalog.GetModelsAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    private static ModelCatalogEntry Enrich(ModelCatalogEntry liveModel, ModelCatalogEntry metadata)
    {
        return liveModel with
        {
            DisplayName = string.IsNullOrWhiteSpace(metadata.DisplayName)
                ? liveModel.DisplayName
                : metadata.DisplayName,
            Source = "provider-live+models.dev",
            ContextWindow = liveModel.ContextWindow ?? metadata.ContextWindow,
            MaxOutputTokens = liveModel.MaxOutputTokens ?? metadata.MaxOutputTokens,
            InputPricePerMillionTokens = liveModel.InputPricePerMillionTokens ?? metadata.InputPricePerMillionTokens,
            OutputPricePerMillionTokens = liveModel.OutputPricePerMillionTokens ?? metadata.OutputPricePerMillionTokens,
            Capabilities = liveModel.Capabilities
                .Concat(metadata.Capabilities)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            IsAvailable = true
        };
    }

    private static string NormalizeModelId(string modelId)
    {
        return modelId.StartsWith("openai/", StringComparison.OrdinalIgnoreCase)
            ? modelId["openai/".Length..]
            : modelId;
    }
}
