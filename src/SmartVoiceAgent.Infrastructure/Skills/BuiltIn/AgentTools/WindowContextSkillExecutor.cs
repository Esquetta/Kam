using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class WindowContextSkillExecutor : ISkillExecutor
{
    private readonly Func<IActiveWindowService> _activeWindowServiceFactory;

    public WindowContextSkillExecutor(IActiveWindowService activeWindowService)
        : this(() => activeWindowService)
    {
    }

    public WindowContextSkillExecutor(IServiceProvider serviceProvider)
        : this(() => serviceProvider.GetRequiredService<IActiveWindowService>())
    {
    }

    private WindowContextSkillExecutor(Func<IActiveWindowService> activeWindowServiceFactory)
    {
        _activeWindowServiceFactory = activeWindowServiceFactory;
    }

    public bool CanExecute(string skillId)
    {
        return skillId.Equals("window.active", StringComparison.OrdinalIgnoreCase)
            || skillId.Equals("window.list", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (plan.SkillId.Equals("window.active", StringComparison.OrdinalIgnoreCase))
        {
            var activeWindowService = _activeWindowServiceFactory();
            return SkillResult.Succeeded(FormatActiveWindow(await activeWindowService.GetActiveWindowInfoAsync()));
        }

        if (plan.SkillId.Equals("window.list", StringComparison.OrdinalIgnoreCase))
        {
            var maxWindows = Math.Clamp(SkillPlanArgumentReader.GetInt(plan, "maxWindows", 10), 1, 50);
            var activeWindowService = _activeWindowServiceFactory();
            var windows = await activeWindowService.GetAllWindowsAsync();
            return SkillResult.Succeeded(FormatWindowList(windows, maxWindows));
        }

        return SkillResult.Failed($"Unsupported window context skill: {plan.SkillId}");
    }

    private static string FormatActiveWindow(ActiveWindowInfo? window)
    {
        return window is null
            ? "Active Window: none"
            : $"Active Window:{Environment.NewLine}{FormatWindow(window)}";
    }

    private static string FormatWindowList(IEnumerable<ActiveWindowInfo> windows, int maxWindows)
    {
        var visibleWindows = windows
            .Where(window => window.IsVisible || window.HasFocus || !string.IsNullOrWhiteSpace(window.Title))
            .Take(maxWindows)
            .ToArray();
        if (visibleWindows.Length == 0)
        {
            return "Visible Windows: none";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Visible Windows ({visibleWindows.Length}):");
        foreach (var window in visibleWindows)
        {
            builder.AppendLine($"- {FormatWindow(window)}");
        }

        return builder.ToString();
    }

    private static string FormatWindow(ActiveWindowInfo window)
    {
        var bounds = window.WindowBounds;
        return $"{window.Title} | Process: {window.ProcessName} | PID: {window.ProcessId} | Bounds: {bounds.X},{bounds.Y},{bounds.Width}x{bounds.Height}";
    }
}
