using SmartVoiceAgent.Core.Models.Updates;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IApplicationUpdateSession
{
    ApplicationUpdateCheckResult? LastCheck { get; }

    ApplicationUpdatePackageSessionState? LastVerifiedPackage { get; }

    void RecordCheck(ApplicationUpdateCheckResult? check);

    void RecordDownload(ApplicationUpdateDownloadResult download);

    void ClearDownload();

    ApplicationUpdatePackageValidationResult ValidateRestartPackage(string? updatePackagePath);
}
