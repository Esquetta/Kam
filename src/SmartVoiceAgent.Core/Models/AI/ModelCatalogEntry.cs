namespace SmartVoiceAgent.Core.Models.AI;

public sealed record ModelCatalogEntry
{
    public ModelProviderType Provider { get; init; }

    public string ProviderId { get; init; } = string.Empty;

    public string ModelId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public int? ContextWindow { get; init; }

    public int? MaxOutputTokens { get; init; }

    public decimal? InputPricePerMillionTokens { get; init; }

    public decimal? OutputPricePerMillionTokens { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public bool IsAvailable { get; init; }

    public DateTimeOffset LastCheckedAt { get; init; } = DateTimeOffset.UtcNow;
}
