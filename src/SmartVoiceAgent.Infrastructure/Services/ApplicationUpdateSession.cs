using System.Security.Cryptography;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Updates;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class ApplicationUpdateSession : IApplicationUpdateSession
{
    private readonly object _syncRoot = new();
    private ApplicationUpdateCheckResult? _lastCheck;
    private ApplicationUpdatePackageSessionState? _lastVerifiedPackage;

    public ApplicationUpdateCheckResult? LastCheck
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastCheck;
            }
        }
    }

    public ApplicationUpdatePackageSessionState? LastVerifiedPackage
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastVerifiedPackage;
            }
        }
    }

    public void RecordCheck(ApplicationUpdateCheckResult? check)
    {
        lock (_syncRoot)
        {
            _lastCheck = check;
        }
    }

    public void RecordDownload(ApplicationUpdateDownloadResult download)
    {
        if (!download.Success
            || !download.IsVerified
            || string.IsNullOrWhiteSpace(download.FilePath))
        {
            ClearDownload();
            return;
        }

        lock (_syncRoot)
        {
            _lastVerifiedPackage = new ApplicationUpdatePackageSessionState(
                NormalizePath(download.FilePath),
                download.Version,
                download.SizeBytes,
                download.VerificationStatus,
                NormalizeSha256(download.ExpectedSha256),
                NormalizeSha256(download.ActualSha256),
                DateTimeOffset.UtcNow);
        }
    }

    public void ClearDownload()
    {
        lock (_syncRoot)
        {
            _lastVerifiedPackage = null;
        }
    }

    public ApplicationUpdatePackageValidationResult ValidateRestartPackage(string? updatePackagePath)
    {
        if (string.IsNullOrWhiteSpace(updatePackagePath))
        {
            return new ApplicationUpdatePackageValidationResult(
                true,
                "No update package was selected for restart handoff.");
        }

        string normalizedPackagePath;
        try
        {
            normalizedPackagePath = NormalizePath(updatePackagePath);
        }
        catch (ArgumentException)
        {
            return Block(updatePackagePath, "Update package path is invalid.");
        }
        catch (NotSupportedException)
        {
            return Block(updatePackagePath, "Update package path is not supported.");
        }

        var lastPackage = LastVerifiedPackage;
        if (lastPackage is null
            || !string.Equals(lastPackage.FilePath, normalizedPackagePath, StringComparison.OrdinalIgnoreCase))
        {
            return Block(
                normalizedPackagePath,
                "Restart handoff requires a verified package downloaded in the current session.");
        }

        if (!File.Exists(normalizedPackagePath))
        {
            return Block(normalizedPackagePath, "Verified update package could not be found.");
        }

        var expectedSha256 = lastPackage.ExpectedSha256 ?? lastPackage.ActualSha256;
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actualSha256 = ComputeSha256(normalizedPackagePath);
            if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Block(normalizedPackagePath, "Verified update package SHA256 no longer matches.");
            }
        }

        if (lastPackage.SizeBytes is not null
            && new FileInfo(normalizedPackagePath).Length != lastPackage.SizeBytes.Value)
        {
            return Block(normalizedPackagePath, "Verified update package size no longer matches.");
        }

        return new ApplicationUpdatePackageValidationResult(
            true,
            "Verified update package is ready for restart handoff.",
            normalizedPackagePath);
    }

    private static ApplicationUpdatePackageValidationResult Block(string packagePath, string message)
    {
        return new ApplicationUpdatePackageValidationResult(false, message, packagePath);
    }

    private static string NormalizePath(string value)
    {
        return Path.GetFullPath(value.Trim());
    }

    private static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length == 64 ? normalized : null;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
