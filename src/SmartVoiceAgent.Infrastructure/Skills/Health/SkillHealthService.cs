using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Health;

public sealed class SkillHealthService : ISkillHealthService
{
    private readonly ISkillRegistry _skillRegistry;
    private readonly IEnumerable<ISkillExecutor> _executors;

    public SkillHealthService(ISkillRegistry skillRegistry, IEnumerable<ISkillExecutor> executors)
    {
        _skillRegistry = skillRegistry;
        _executors = executors;
    }

    public Task<IReadOnlyCollection<SkillHealthReport>> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyCollection<SkillHealthReport> reports = _skillRegistry
            .GetAll()
            .OrderBy(manifest => manifest.Id, StringComparer.OrdinalIgnoreCase)
            .Select(CreateReport)
            .ToArray();

        return Task.FromResult(reports);
    }

    private SkillHealthReport CreateReport(KamSkillManifest manifest)
    {
        var status = GetStatus(manifest);
        var missingPermissions = GetMissingPermissions(manifest);

        return new SkillHealthReport
        {
            SkillId = manifest.Id,
            DisplayName = manifest.DisplayName,
            Description = manifest.Description,
            Source = manifest.Source,
            ExecutorType = manifest.ExecutorType,
            RiskLevel = manifest.RiskLevel,
            Status = status,
            Details = GetDetails(status, missingPermissions)
        };
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
        var required = manifest.Permissions
            .Where(permission => permission != SkillPermission.None)
            .Distinct()
            .ToArray();

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
}
