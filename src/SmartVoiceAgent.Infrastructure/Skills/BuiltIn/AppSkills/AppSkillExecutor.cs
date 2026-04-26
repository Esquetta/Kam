using System.Text.Json;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AppSkills;

public sealed class AppSkillExecutor : ISkillExecutor
{
    private readonly IApplicationService _applicationService;

    public AppSkillExecutor(IApplicationService applicationService)
    {
        _applicationService = applicationService;
    }

    public bool CanExecute(string skillId)
    {
        return skillId.Equals("apps.open", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("apps.close", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("apps.status", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("apps.list", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SkillResult> ExecuteAsync(SkillPlan plan, CancellationToken cancellationToken = default)
    {
        var skillId = plan.SkillId.ToLowerInvariant();
        switch (skillId)
        {
            case "apps.open":
            {
                var appName = GetRequiredApplicationName(plan);
                if (appName is null)
                {
                    return SkillResult.Failed("applicationName is required.");
                }

                await _applicationService.OpenApplicationAsync(appName);
                return SkillResult.Succeeded($"Opened {appName}.");
            }

            case "apps.close":
            {
                var appName = GetRequiredApplicationName(plan);
                if (appName is null)
                {
                    return SkillResult.Failed("applicationName is required.");
                }

                await _applicationService.CloseApplicationAsync(appName);
                return SkillResult.Succeeded($"Closed {appName}.");
            }

            case "apps.status":
            {
                var appName = GetRequiredApplicationName(plan);
                if (appName is null)
                {
                    return SkillResult.Failed("applicationName is required.");
                }

                var status = await _applicationService.GetApplicationStatusAsync(appName);
                return SkillResult.Succeeded($"{appName} status: {status}.");
            }

            case "apps.list":
            {
                var applications = (await _applicationService.ListApplicationsAsync()).ToList();
                return SkillResult.Succeeded(
                    $"Found {applications.Count} applications.",
                    applications);
            }

            default:
                return SkillResult.Failed($"Unsupported app skill: {plan.SkillId}");
        }
    }

    private static string? GetRequiredApplicationName(SkillPlan plan)
    {
        if (!plan.Arguments.TryGetValue("applicationName", out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }
}
