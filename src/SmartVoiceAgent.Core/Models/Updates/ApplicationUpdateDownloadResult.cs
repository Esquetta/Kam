namespace SmartVoiceAgent.Core.Models.Updates;

public sealed record ApplicationUpdateDownloadResult(
    bool Success,
    string Message,
    string? FilePath = null,
    string? Version = null,
    long? SizeBytes = null)
{
    public static ApplicationUpdateDownloadResult Failed(string message)
    {
        return new ApplicationUpdateDownloadResult(false, message);
    }

    public static ApplicationUpdateDownloadResult Succeeded(
        string filePath,
        string? version,
        long sizeBytes)
    {
        return new ApplicationUpdateDownloadResult(
            true,
            $"Downloaded Kam update package to {filePath}.",
            filePath,
            version,
            sizeBytes);
    }
}
