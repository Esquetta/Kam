using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Health;

public sealed class SkillHealthService : ISkillHealthService
{
    private const int AuditHistoryLimit = 500;
    private const int RecentRunHistoryLimit = 5;

    private readonly ISkillRegistry _skillRegistry;
    private readonly IEnumerable<ISkillExecutor> _executors;
    private readonly ISkillAuditLogService? _auditLogService;

    public SkillHealthService(
        ISkillRegistry skillRegistry,
        IEnumerable<ISkillExecutor> executors,
        ISkillAuditLogService? auditLogService = null)
    {
        _skillRegistry = skillRegistry;
        _executors = executors;
        _auditLogService = auditLogService;
    }

    public async Task<IReadOnlyCollection<SkillHealthReport>> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var recentRuns = await GetRecentRunsAsync(cancellationToken);
        IReadOnlyCollection<SkillHealthReport> reports = _skillRegistry
            .GetAll()
            .OrderBy(manifest => manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Select(manifest =>
            {
                recentRuns.TryGetValue(manifest.Id, out var manifestRuns);
                return CreateReport(manifest, manifestRuns ?? []);
            })
            .ToArray();

        return reports;
    }

    private SkillHealthReport CreateReport(
        KamSkillManifest manifest,
        IReadOnlyList<SkillAuditRecord> recentRuns)
    {
        var status = GetStatus(manifest);
        var missingPermissions = GetMissingPermissions(manifest);
        var lastRun = recentRuns.FirstOrDefault();
        var successfulRuns = recentRuns
            .Count(run => run.Status == SkillExecutionStatus.Succeeded);
        var failedRuns = recentRuns.Count - successfulRuns;
        var measuredDurations = recentRuns
            .Where(run => run.DurationMilliseconds > 0)
            .Select(run => run.DurationMilliseconds)
            .ToArray();
        var lastFailure = recentRuns.FirstOrDefault(run =>
            run.Status != SkillExecutionStatus.Succeeded);

        return new SkillHealthReport
        {
            SkillId = manifest.Id,
            DisplayName = manifest.DisplayName,
            Description = manifest.Description,
            Source = manifest.Source,
            ExecutorType = manifest.ExecutorType,
            Checksum = manifest.Checksum,
            InstalledFrom = manifest.InstalledFrom,
            InstalledAt = manifest.InstalledAt,
            RiskLevel = manifest.RiskLevel,
            Status = status,
            Details = GetDetails(status, missingPermissions),
            RequiredPermissions = GetRequiredPermissions(manifest),
            GrantedPermissions = manifest.GrantedPermissions
                .Where(permission => permission != SkillPermission.None)
                .Distinct()
                .ToArray(),
            MissingPermissions = missingPermissions,
            RuntimeOptions = manifest.RuntimeOptions
                .Where(option => !string.IsNullOrWhiteSpace(option.Key))
                .ToDictionary(
                    option => option.Key,
                    option => option.Value,
                    StringComparer.OrdinalIgnoreCase),
            LastRunAt = lastRun?.Timestamp,
            LastRunStatus = lastRun?.Status,
            LastRunMessage = lastRun?.ResultMessage ?? string.Empty,
            LastRunErrorCode = lastRun?.ErrorCode ?? string.Empty,
            LastRunDurationMilliseconds = lastRun?.DurationMilliseconds ?? 0,
            RecentRuns = recentRuns.ToArray(),
            RecentRunCount = recentRuns.Count,
            RecentSuccessCount = successfulRuns,
            RecentFailureCount = failedRuns,
            RecentSuccessRatePercent = recentRuns.Count == 0
                ? 0
                : successfulRuns * 100d / recentRuns.Count,
            RecentAverageDurationMilliseconds = measuredDurations.Length == 0
                ? 0
                : Convert.ToInt64(Math.Round(measuredDurations.Average())),
            LastFailureAt = lastFailure?.Timestamp,
            LastFailureMessage = lastFailure?.ResultMessage ?? string.Empty,
            LastFailureErrorCode = lastFailure?.ErrorCode ?? string.Empty
        };
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<SkillAuditRecord>>> GetRecentRunsAsync(
        CancellationToken cancellationToken)
    {
        if (_auditLogService is null)
        {
            return new Dictionary<string, IReadOnlyList<SkillAuditRecord>>(StringComparer.OrdinalIgnoreCase);
        }

        var records = await _auditLogService.GetRecentAsync(AuditHistoryLimit, cancellationToken);
        return records
            .Where(record => !string.IsNullOrWhiteSpace(record.SkillId))
            .OrderByDescending(record => record.Timestamp)
            .GroupBy(record => record.SkillId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SkillAuditRecord>)group.Take(RecentRunHistoryLimit).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private SkillHealthStatus GetStatus(KamSkillManifest manifest)
    {
        if (manifest.ReviewRequired)
        {
            return SkillHealthStatus.ReviewRequired;
        }

        if (!manifest.Enabled)
        {
            return SkillHealthStatus.Disabled;
        }

        if (GetMissingPermissions(manifest).Count > 0)
        {
            return SkillHealthStatus.PermissionDenied;
        }

        return _executors.Any(executor => executor.CanExecute(manifest.Id))
            ? SkillHealthStatus.Healthy
            : SkillHealthStatus.MissingExecutor;
    }

    private static string GetDetails(
        SkillHealthStatus status,
        IReadOnlyCollection<SkillPermission> missingPermissions)
    {
        return status switch
        {
            SkillHealthStatus.Healthy => "Executor available.",
            SkillHealthStatus.Disabled => "Skill is disabled.",
            SkillHealthStatus.MissingExecutor => "No executor registered for this skill.",
            SkillHealthStatus.ReviewRequired => "Skill requires review before it can be enabled.",
            SkillHealthStatus.PermissionDenied => $"Missing granted permissions: {string.Join(", ", missingPermissions)}.",
            _ => "Unknown skill health state."
        };
    }

    private static IReadOnlyCollection<SkillPermission> GetMissingPermissions(KamSkillManifest manifest)
    {
        var required = GetRequiredPermissions(manifest);

        if (required.Length == 0)
        {
            return [];
        }

        var granted = manifest.GrantedPermissions
            .Where(permission => permission != SkillPermission.None)
            .Distinct()
            .ToHashSet();

        return required
            .Where(permission => !granted.Contains(permission))
            .ToArray();
    }

    private static SkillPermission[] GetRequiredPermissions(KamSkillManifest manifest)
    {
        return manifest.Permissions
            .Where(permission => permission != SkillPermission.None)
            .Distinct()
            .ToArray();
    }
}
