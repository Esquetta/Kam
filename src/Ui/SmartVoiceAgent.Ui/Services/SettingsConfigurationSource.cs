using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Puts model and integration settings into the app configuration, above appsettings and user secrets.
/// </summary>
public sealed class SettingsConfigurationSource : IConfigurationSource
{
    /// <summary>
    /// Creates the source with its first values.
    /// </summary>
    /// <param name="values">Configuration keys and values taken from Settings.</param>
    public SettingsConfigurationSource(IReadOnlyDictionary<string, string?> values)
    {
        Provider = new SettingsConfigurationProvider(values);
    }

    /// <summary>
    /// Gets the provider, which <see cref="SettingsConfigurationProvider.Reload"/> updates when Settings change.
    /// </summary>
    public SettingsConfigurationProvider Provider { get; }

    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
}

/// <summary>
/// Configuration values taken from Settings. A reload raises the configuration change token, so options
/// monitors and services that read configuration per use see the new values without a restart.
/// </summary>
public sealed class SettingsConfigurationProvider : ConfigurationProvider
{
    private IReadOnlyDictionary<string, string?> _values;

    /// <summary>
    /// Creates the provider.
    /// </summary>
    /// <param name="values">Configuration keys and values taken from Settings.</param>
    public SettingsConfigurationProvider(IReadOnlyDictionary<string, string?> values)
    {
        _values = values;
    }

    /// <inheritdoc />
    public override void Load()
    {
        Data = new Dictionary<string, string?>(_values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Replaces the values and notifies configuration listeners when any of them changed.
    /// </summary>
    /// <param name="values">The values for the current Settings.</param>
    /// <returns>The keys that were added, removed or changed; empty when nothing changed.</returns>
    public IReadOnlyList<string> Reload(IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var current = new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase);
        var next = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        var changed = current.Keys
            .Union(next.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(key => !current.TryGetValue(key, out var before)
                || !next.TryGetValue(key, out var after)
                || !string.Equals(before, after, StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (changed.Count == 0)
        {
            return changed;
        }

        _values = values;
        Data = next;
        OnReload();
        return changed;
    }
}
