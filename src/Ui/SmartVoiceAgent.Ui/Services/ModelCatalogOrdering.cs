using System;
using System.Collections.Generic;
using System.Linq;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Orders model lists the way Settings shows them: newest first.
/// </summary>
public static class ModelCatalogOrdering
{
    /// <summary>
    /// Returns the models newest first by release date; models without a date follow, by id descending.
    /// </summary>
    /// <param name="models">The models to order.</param>
    public static ModelCatalogEntry[] NewestFirst(this IEnumerable<ModelCatalogEntry> models)
    {
        ArgumentNullException.ThrowIfNull(models);

        return models
            .OrderByDescending(model => model.ReleasedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(model => model.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
