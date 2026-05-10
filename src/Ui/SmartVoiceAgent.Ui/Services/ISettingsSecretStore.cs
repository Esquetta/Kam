using System.Collections.Generic;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Stores sensitive settings outside the main settings JSON file.
/// </summary>
public interface ISettingsSecretStore
{
    /// <summary>
    /// Gets a secret by name.
    /// </summary>
    string? GetSecret(string name);

    /// <summary>
    /// Gets all stored secret names.
    /// </summary>
    IReadOnlyCollection<string> GetSecretNames();

    /// <summary>
    /// Stores or replaces a secret value.
    /// </summary>
    void SetSecret(string name, string value);

    /// <summary>
    /// Removes a secret by name.
    /// </summary>
    void RemoveSecret(string name);
}
