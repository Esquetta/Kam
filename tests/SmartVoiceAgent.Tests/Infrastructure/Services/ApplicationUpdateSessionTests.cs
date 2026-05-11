using FluentAssertions;
using SmartVoiceAgent.Core.Models.Updates;
using SmartVoiceAgent.Infrastructure.Services;
using System.Security.Cryptography;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class ApplicationUpdateSessionTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-update-session-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void RecordCheck_StoresLatestUpdateCheckResult()
    {
        var session = new ApplicationUpdateSession();
        var check = ApplicationUpdateCheckResult.UpdateAvailable(
            "1.0.0",
            "1.2.0",
            "Kam 1.2.0",
            "https://github.com/Esquetta/Kam/releases/tag/v1.2.0",
            DateTimeOffset.Parse("2026-05-09T12:00:00Z"),
            asset: null);

        session.RecordCheck(check);

        session.LastCheck.Should().BeSameAs(check);
    }

    [Fact]
    public void ValidateRestartPackage_WhenVerifiedPackageIsUnchanged_AllowsRestart()
    {
        var packagePath = CreatePackage("Kam-1.2.0-x64.msi", "verified-package");
        var sha256 = ComputeSha256(packagePath);
        var session = new ApplicationUpdateSession();

        session.RecordDownload(ApplicationUpdateDownloadResult.Succeeded(
            packagePath,
            "1.2.0",
            new FileInfo(packagePath).Length,
            isVerified: true,
            verificationStatus: "SHA256 verified",
            expectedSha256: sha256,
            actualSha256: sha256));

        var validation = session.ValidateRestartPackage(packagePath);

        validation.CanRestart.Should().BeTrue();
        validation.NormalizedPackagePath.Should().Be(Path.GetFullPath(packagePath));
        validation.Message.Should().Contain("ready");
    }

    [Fact]
    public void ValidateRestartPackage_WhenVerifiedPackageWasModified_BlocksRestart()
    {
        var packagePath = CreatePackage("Kam-1.2.0-x64.msi", "verified-package");
        var sha256 = ComputeSha256(packagePath);
        var session = new ApplicationUpdateSession();
        session.RecordDownload(ApplicationUpdateDownloadResult.Succeeded(
            packagePath,
            "1.2.0",
            new FileInfo(packagePath).Length,
            isVerified: true,
            verificationStatus: "SHA256 verified",
            expectedSha256: sha256,
            actualSha256: sha256));
        File.AppendAllText(packagePath, "-tampered");

        var validation = session.ValidateRestartPackage(packagePath);

        validation.CanRestart.Should().BeFalse();
        validation.Message.Should().Contain("SHA256 no longer matches");
    }

    [Fact]
    public void ValidateRestartPackage_WhenVerifiedPackageWasDeleted_BlocksRestart()
    {
        var packagePath = CreatePackage("Kam-1.2.0-x64.msi", "verified-package");
        var sha256 = ComputeSha256(packagePath);
        var session = new ApplicationUpdateSession();
        session.RecordDownload(ApplicationUpdateDownloadResult.Succeeded(
            packagePath,
            "1.2.0",
            new FileInfo(packagePath).Length,
            isVerified: true,
            verificationStatus: "SHA256 verified",
            expectedSha256: sha256,
            actualSha256: sha256));
        File.Delete(packagePath);

        var validation = session.ValidateRestartPackage(packagePath);

        validation.CanRestart.Should().BeFalse();
        validation.Message.Should().Contain("could not be found");
    }

    [Fact]
    public void RecordDownload_WhenPackageIsNotVerified_ClearsVerifiedRestartState()
    {
        var packagePath = CreatePackage("Kam-1.2.0-x64.msi", "verified-package");
        var session = new ApplicationUpdateSession();

        session.RecordDownload(ApplicationUpdateDownloadResult.Succeeded(
            packagePath,
            "1.2.0",
            new FileInfo(packagePath).Length,
            isVerified: false,
            verificationStatus: "Checksum missing"));

        var validation = session.ValidateRestartPackage(packagePath);

        validation.CanRestart.Should().BeFalse();
        validation.Message.Should().Contain("verified package downloaded in the current session");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private string CreatePackage(string fileName, string contents)
    {
        Directory.CreateDirectory(_testDirectory);
        var packagePath = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(packagePath, contents);
        return packagePath;
    }

    private static string ComputeSha256(string filePath)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant();
    }
}
