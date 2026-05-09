using SmartVoiceAgent.Core.Models.Updates;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IApplicationUpdateService
{
    string CurrentVersion { get; }

    Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default);

    Task<ApplicationUpdateDownloadResult> DownloadLatestAsync(
        CancellationToken cancellationToken = default);
}
