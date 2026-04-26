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

        return new SkillHealthReport
        {
            SkillId = manifest.Id,
            DisplayName = manifest.DisplayName,
            Description = manifest.Description,
            Source = manifest.Source,
            ExecutorType = manifest.ExecutorType,
            RiskLevel = manifest.RiskLevel,
            Status = status,
            Details = GetDetails(status)
        };
    }

    private SkillHealthStatus GetStatus(KamSkillManifest manifest)
    {
        if (!manifest.Enabled)
        {
            return SkillHealthStatus.Disabled;
        }

        return _executors.Any(executor => executor.CanExecute(manifest.Id))
            ? SkillHealthStatus.Healthy
            : SkillHealthStatus.MissingExecutor;
    }

    private static string GetDetails(SkillHealthStatus status)
    {
        return status switch
        {
            SkillHealthStatus.Healthy => "Executor available.",
            SkillHealthStatus.Disabled => "Skill is disabled.",
            SkillHealthStatus.MissingExecutor => "No executor registered for this skill.",
            _ => "Unknown skill health state."
        };
    }
}
