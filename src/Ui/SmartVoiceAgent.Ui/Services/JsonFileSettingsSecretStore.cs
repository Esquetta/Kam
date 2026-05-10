using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// File-backed local secret store for settings values that must not be written to settings.json.
/// </summary>
public sealed class JsonFileSettingsSecretStore : ISettingsSecretStore
{
    private const string DpapiPrefix = "dpapi:";
    private const string AesGcmPrefix = "aesgcm:";
    private const int AesKeySize = 32;
    private const int AesNonceSize = 12;
    private const int AesTagSize = 16;

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kam.JsonSettingsService.v1");

    private readonly string _secretsPath;
    private readonly string _fallbackKeyPath;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonFileSettingsSecretStore"/> class.
    /// </summary>
    public JsonFileSettingsSecretStore(string settingsDirectory)
    {
        Directory.CreateDirectory(settingsDirectory);
        _secretsPath = Path.Combine(settingsDirectory, "settings.secrets.json");
        _fallbackKeyPath = Path.Combine(settingsDirectory, "settings.secrets.key");
    }

    /// <inheritdoc />
    public string? GetSecret(string name)
    {
        lock (_lock)
        {
            var secrets = LoadSecrets();
            return secrets.TryGetValue(name, out var protectedValue)
                ? Unprotect(protectedValue)
                : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetSecretNames()
    {
        lock (_lock)
        {
            return LoadSecrets().Keys.ToArray();
        }
    }

    /// <inheritdoc />
    public void SetSecret(string name, string value)
    {
        lock (_lock)
        {
            var secrets = LoadSecrets();
            secrets[name] = Protect(value);
            SaveSecrets(secrets);
        }
    }

    /// <inheritdoc />
    public void RemoveSecret(string name)
    {
        lock (_lock)
        {
            var secrets = LoadSecrets();
            if (secrets.Remove(name))
            {
                SaveSecrets(secrets);
            }
        }
    }

    private Dictionary<string, string> LoadSecrets()
    {
        if (!File.Exists(_secretsPath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_secretsPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings secrets: {ex}");
            return [];
        }
    }

    private void SaveSecrets(Dictionary<string, string> secrets)
    {
        if (secrets.Count == 0)
        {
            if (File.Exists(_secretsPath))
            {
                PrepareExistingFileForWrite(_secretsPath);
                File.Delete(_secretsPath);
            }

            return;
        }

        var json = JsonSerializer.Serialize(secrets, new JsonSerializerOptions { WriteIndented = true });
        PrepareExistingFileForWrite(_secretsPath);
        File.WriteAllText(_secretsPath, json);
        RestrictFileAccess(_secretsPath);
    }

    private string Protect(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (OperatingSystem.IsWindows())
        {
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(protectedBytes);
        }

        var key = GetOrCreateFallbackKey();
        var nonce = RandomNumberGenerator.GetBytes(AesNonceSize);
        var ciphertext = new byte[bytes.Length];
        var tag = new byte[AesTagSize];

        using var aes = new AesGcm(key, AesTagSize);
        aes.Encrypt(nonce, bytes, ciphertext, tag, Entropy);

        var payload = new byte[AesNonceSize + AesTagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, AesNonceSize);
        Buffer.BlockCopy(tag, 0, payload, AesNonceSize, AesTagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, AesNonceSize + AesTagSize, ciphertext.Length);

        return AesGcmPrefix + Convert.ToBase64String(payload);
    }

    private string Unprotect(string protectedValue)
    {
        if (protectedValue.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindows())
            {
                return string.Empty;
            }

            var payload = Convert.FromBase64String(protectedValue[DpapiPrefix.Length..]);
            var bytes = ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }

        if (protectedValue.StartsWith(AesGcmPrefix, StringComparison.Ordinal))
        {
            var payload = Convert.FromBase64String(protectedValue[AesGcmPrefix.Length..]);
            var nonce = payload[..AesNonceSize];
            var tag = payload[AesNonceSize..(AesNonceSize + AesTagSize)];
            var ciphertext = payload[(AesNonceSize + AesTagSize)..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(GetOrCreateFallbackKey(), AesTagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, Entropy);
            return Encoding.UTF8.GetString(plaintext);
        }

        return string.Empty;
    }

    private byte[] GetOrCreateFallbackKey()
    {
        if (File.Exists(_fallbackKeyPath))
        {
            return Convert.FromBase64String(File.ReadAllText(_fallbackKeyPath));
        }

        var key = RandomNumberGenerator.GetBytes(AesKeySize);
        File.WriteAllText(_fallbackKeyPath, Convert.ToBase64String(key));
        RestrictFileAccess(_fallbackKeyPath);
        return key;
    }

    private static void PrepareExistingFileForWrite(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.Hidden);
    }

    private static void RestrictFileAccess(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
            }
            else
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to restrict settings secret file access: {ex}");
        }
    }
}
