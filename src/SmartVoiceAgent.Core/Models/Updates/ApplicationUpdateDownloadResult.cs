namespace SmartVoiceAgent.Core.Models.Updates;

public sealed record ApplicationUpdateDownloadResult(
    bool Success,
    string Message,
    string? FilePath = null,
    string? Version = null,
    long? SizeBytes = null,
    bool IsVerified = false,
    string VerificationStatus = "Not verified",
    string? ExpectedSha256 = null,
    string? ActualSha256 = null)
{
    public static ApplicationUpdateDownloadResult Failed(
        string message,
        string verificationStatus = "Not verified",
        string? filePath = null,
        string? version = null,
        string? expectedSha256 = null,
        string? actualSha256 = null)
    {
        return new ApplicationUpdateDownloadResult(
            false,
            message,
            filePath,
            version,
            null,
            false,
            verificationStatus,
            expectedSha256,
            actualSha256);
    }

    public static ApplicationUpdateDownloadResult Succeeded(
        string filePath,
        string? version,
        long sizeBytes,
        bool isVerified = false,
        string verificationStatus = "Not verified",
        string? expectedSha256 = null,
        string? actualSha256 = null)
    {
        return new ApplicationUpdateDownloadResult(
            true,
            $"Downloaded Kam update package to {filePath}.",
            filePath,
            version,
            sizeBytes,
            isVerified,
            verificationStatus,
            expectedSha256,
            actualSha256);
    }
}
