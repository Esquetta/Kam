using System.Drawing;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Dtos.Screen;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

public sealed class AccessibilitySkillExecutor : ISkillExecutor
{
    private readonly Func<IScreenContextService> _screenContextServiceFactory;
    private readonly Func<IActiveWindowService> _activeWindowServiceFactory;

    public AccessibilitySkillExecutor(
        IScreenContextService screenContextService,
        IActiveWindowService activeWindowService)
        : this(() => screenContextService, () => activeWindowService)
    {
    }

    public AccessibilitySkillExecutor(IServiceProvider serviceProvider)
        : this(
            () => serviceProvider.GetRequiredService<IScreenContextService>(),
            () => serviceProvider.GetRequiredService<IActiveWindowService>())
    {
    }

    private AccessibilitySkillExecutor(
        Func<IScreenContextService> screenContextServiceFactory,
        Func<IActiveWindowService> activeWindowServiceFactory)
    {
        _screenContextServiceFactory = screenContextServiceFactory;
        _activeWindowServiceFactory = activeWindowServiceFactory;
    }

    public bool CanExecute(string skillId)
    {
        return skillId.Equals("accessibility.tree", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SkillResult> ExecuteAsync(
        SkillPlan plan,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(plan.SkillId))
        {
            return SkillResult.Failed($"Unsupported accessibility skill: {plan.SkillId}");
        }

        var maxNodes = Math.Clamp(SkillPlanArgumentReader.GetInt(plan, "maxNodes", 40), 1, 200);
        var maxScreens = Math.Clamp(SkillPlanArgumentReader.GetInt(plan, "maxScreens", 2), 1, 8);
        var includeObjects = SkillPlanArgumentReader.GetBool(plan, "includeObjects", true);

        ActiveWindowInfo? activeWindow = null;
        try
        {
            activeWindow = await _activeWindowServiceFactory().GetActiveWindowInfoAsync();
        }
        catch
        {
            activeWindow = null;
        }

        var screens = await _screenContextServiceFactory().CaptureAndAnalyzeAsync(cancellationToken);
        return SkillResult.Succeeded(FormatTree(screens.Take(maxScreens), activeWindow, maxNodes, includeObjects));
    }

    private static string FormatTree(
        IEnumerable<ScreenContext> screens,
        ActiveWindowInfo? activeWindow,
        int maxNodes,
        bool includeObjects)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Accessibility Tree");
        builder.AppendLine(activeWindow is null
            ? "Active Window: none"
            : $"Active Window: {activeWindow.Title} | Process: {activeWindow.ProcessName} | PID: {activeWindow.ProcessId}");

        var emittedNodes = 0;
        foreach (var screen in screens)
        {
            builder.AppendLine($"Screen {screen.ScreenIndex}: {screen.DeviceName} {screen.Width}x{screen.Height} Primary={screen.IsPrimary}");
            if (screen.ActiveWindow is not null)
            {
                builder.AppendLine($"  Window: {screen.ActiveWindow.Title} | {screen.ActiveWindow.ProcessName}");
            }

            foreach (var line in screen.OcrLines)
            {
                if (emittedNodes >= maxNodes)
                {
                    builder.AppendLine("  ... nodes truncated");
                    return builder.ToString();
                }

                builder.AppendLine($"  Text[{line.LineNumber}]: {line.Text} | Confidence={line.Confidence:0.00} | Box={FormatBox(line.BoundingBox)}");
                emittedNodes++;
            }

            if (!includeObjects)
            {
                continue;
            }

            foreach (var item in screen.Objects)
            {
                if (emittedNodes >= maxNodes)
                {
                    builder.AppendLine("  ... nodes truncated");
                    return builder.ToString();
                }

                builder.AppendLine($"  Object: {item.Label} | Confidence={item.Confidence:0.00} | Box={FormatBox(item.BoundingBox)}");
                emittedNodes++;
            }
        }

        if (emittedNodes == 0)
        {
            builder.AppendLine("  (no OCR or object nodes)");
        }

        return builder.ToString();
    }

    private static string FormatBox(Rectangle rectangle)
    {
        return $"{rectangle.X},{rectangle.Y},{rectangle.Width}x{rectangle.Height}";
    }
}
