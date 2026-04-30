using System.Runtime.InteropServices;
using System.Text.Json;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Dtos.Screen;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Context;

public sealed class SkillRuntimeContextProvider : ISkillRuntimeContextProvider
{
    private const int MaxWindows = 10;
    private const int MaxScreens = 2;
    private const int MaxOcrLines = 12;
    private const int MaxApplications = 25;

    private readonly IActiveWindowService _activeWindowService;
    private readonly IScreenContextService _screenContextService;
    private readonly IApplicationServiceFactory _applicationServiceFactory;

    public SkillRuntimeContextProvider(
        IActiveWindowService activeWindowService,
        IScreenContextService screenContextService,
        IApplicationServiceFactory applicationServiceFactory)
    {
        _activeWindowService = activeWindowService;
        _screenContextService = screenContextService;
        _applicationServiceFactory = applicationServiceFactory;
    }

    public async Task<SkillRuntimeContext> CreateAsync(
        SkillPlan plan,
        CancellationToken cancellationToken = default)
    {
        var context = new SkillRuntimeContext
        {
            UserInput = ResolveUserInput(plan),
            OperatingSystem = RuntimeInformation.OSDescription,
            Timestamp = DateTimeOffset.UtcNow
        };

        await PopulateWindowContextAsync(context);
        await PopulateScreenContextAsync(context, cancellationToken);
        await PopulateApplicationContextAsync(context);

        return context;
    }

    private async Task PopulateWindowContextAsync(SkillRuntimeContext context)
    {
        try
        {
            var activeWindow = await _activeWindowService.GetActiveWindowInfoAsync();
            context.ActiveWindow = activeWindow is null ? null : MapWindow(activeWindow);
        }
        catch
        {
            context.ActiveWindow = null;
        }

        try
        {
            var windows = await _activeWindowService.GetAllWindowsAsync();
            context.VisibleWindows = windows
                .Where(window => window.IsVisible || window.HasFocus || !string.IsNullOrWhiteSpace(window.Title))
                .Take(MaxWindows)
                .Select(MapWindow)
                .ToList();
        }
        catch
        {
            context.VisibleWindows = context.ActiveWindow is null ? [] : [context.ActiveWindow];
        }
    }

    private async Task PopulateScreenContextAsync(
        SkillRuntimeContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var screens = await _screenContextService.CaptureAndAnalyzeAsync(cancellationToken);
            context.Screens = screens
                .Take(MaxScreens)
                .Select(MapScreen)
                .ToList();
        }
        catch
        {
            context.Screens = [];
        }
    }

    private async Task PopulateApplicationContextAsync(SkillRuntimeContext context)
    {
        try
        {
            var service = _applicationServiceFactory.Create();
            var applications = await service.ListApplicationsAsync();
            context.InstalledApplications = applications
                .Select(application => application.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .Take(MaxApplications)
                .ToList();
        }
        catch
        {
            context.InstalledApplications = [];
        }
    }

    private static string ResolveUserInput(SkillPlan plan)
    {
        if (plan.Arguments.TryGetValue("input", out var input)
            && input.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(input.GetString()))
        {
            return input.GetString()!;
        }

        if (!string.IsNullOrWhiteSpace(plan.Reasoning))
        {
            return plan.Reasoning;
        }

        return JsonSerializer.Serialize(plan.Arguments);
    }

    private static SkillRuntimeWindow MapWindow(ActiveWindowInfo window)
    {
        return new SkillRuntimeWindow
        {
            Title = window.Title,
            ProcessName = window.ProcessName,
            ProcessId = window.ProcessId,
            HasFocus = window.HasFocus,
            IsVisible = window.IsVisible
        };
    }

    private static SkillRuntimeScreenContext MapScreen(ScreenContext screen)
    {
        return new SkillRuntimeScreenContext
        {
            ScreenIndex = screen.ScreenIndex,
            DeviceName = screen.DeviceName ?? string.Empty,
            IsPrimary = screen.IsPrimary,
            Width = screen.Width,
            Height = screen.Height,
            OcrLines = screen.OcrLines
                .Select(line => line.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Take(MaxOcrLines)
                .ToList()
        };
    }
}
