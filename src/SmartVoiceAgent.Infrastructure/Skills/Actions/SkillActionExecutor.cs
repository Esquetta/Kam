using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;

namespace SmartVoiceAgent.Infrastructure.Skills.Actions;

public sealed class SkillActionExecutor : ISkillActionExecutor
{
    private readonly IApplicationServiceFactory _applicationServiceFactory;
    private readonly IDesktopAutomationAdapter _desktopAutomationAdapter;
    private readonly ClipboardTools? _clipboardTools;
    private readonly IScreenContextService? _screenContextService;

    public SkillActionExecutor(
        IApplicationServiceFactory applicationServiceFactory,
        IDesktopAutomationAdapter desktopAutomationAdapter,
        ClipboardTools? clipboardTools = null,
        IScreenContextService? screenContextService = null)
    {
        _applicationServiceFactory = applicationServiceFactory;
        _desktopAutomationAdapter = desktopAutomationAdapter;
        _clipboardTools = clipboardTools;
        _screenContextService = screenContextService;
    }

    public async Task<SkillActionExecutionResult> ExecuteAsync(
        SkillActionPlan plan,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SkillActionStepResult>();

        foreach (var action in plan.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await ExecuteStepAsync(action, cancellationToken);
            results.Add(result);

            if (!result.Success)
            {
                return SkillActionExecutionResult.Failed(
                    CombineMessage(plan.Message, result.Message),
                    results);
            }
        }

        return SkillActionExecutionResult.Succeeded(
            CombineMessage(plan.Message, results.LastOrDefault()?.Message ?? "No actions were required."),
            results);
    }

    private async Task<SkillActionStepResult> ExecuteStepAsync(
        SkillActionStep action,
        CancellationToken cancellationToken)
    {
        return action.Type.ToLowerInvariant() switch
        {
            SkillActionTypes.Respond => SkillActionStepResult.Succeeded(
                SkillActionTypes.Respond,
                string.IsNullOrWhiteSpace(action.Text) ? "Response prepared." : action.Text),

            SkillActionTypes.OpenApp => await OpenAppAsync(action),

            SkillActionTypes.FocusWindow => await _desktopAutomationAdapter.FocusWindowAsync(
                ResolveTarget(action),
                cancellationToken),

            SkillActionTypes.Click => action.X.HasValue && action.Y.HasValue
                ? await _desktopAutomationAdapter.ClickAsync(action.X.Value, action.Y.Value, cancellationToken)
                : SkillActionStepResult.Failed(action.Type, "Click action requires x and y coordinates.", "missing_coordinates"),

            SkillActionTypes.TypeText => await _desktopAutomationAdapter.TypeTextAsync(
                action.Text,
                cancellationToken),

            SkillActionTypes.Hotkey => action.Keys.Count > 0
                ? await _desktopAutomationAdapter.HotkeyAsync(action.Keys, cancellationToken)
                : SkillActionStepResult.Failed(action.Type, "Hotkey action requires at least one key.", "missing_keys"),

            SkillActionTypes.ClipboardSet => await SetClipboardAsync(action),

            SkillActionTypes.ClipboardGet => await GetClipboardAsync(action),

            SkillActionTypes.ReadScreen => await ReadScreenAsync(cancellationToken),

            _ => SkillActionStepResult.Failed(
                action.Type,
                $"Unsupported action '{action.Type}'.",
                "unsupported_action")
        };
    }

    private async Task<SkillActionStepResult> SetClipboardAsync(SkillActionStep action)
    {
        if (_clipboardTools is null)
        {
            return SkillActionStepResult.Failed(
                action.Type,
                "Clipboard service is unavailable.",
                "clipboard_unavailable");
        }

        if (string.IsNullOrWhiteSpace(action.Text))
        {
            return SkillActionStepResult.Failed(
                action.Type,
                "Clipboard set action requires text.",
                "missing_text");
        }

        var result = await _clipboardTools.SetClipboardAsync(action.Text);
        return SkillActionStepResult.Succeeded(action.Type, result);
    }

    private async Task<SkillActionStepResult> GetClipboardAsync(SkillActionStep action)
    {
        if (_clipboardTools is null)
        {
            return SkillActionStepResult.Failed(
                action.Type,
                "Clipboard service is unavailable.",
                "clipboard_unavailable");
        }

        var result = await _clipboardTools.GetClipboardAsync();
        return SkillActionStepResult.Succeeded(action.Type, result);
    }

    private async Task<SkillActionStepResult> ReadScreenAsync(CancellationToken cancellationToken)
    {
        if (_screenContextService is null)
        {
            return SkillActionStepResult.Failed(
                SkillActionTypes.ReadScreen,
                "Screen context service is unavailable.",
                "screen_context_unavailable");
        }

        var screens = await _screenContextService.CaptureAndAnalyzeAsync(cancellationToken);
        return SkillActionStepResult.Succeeded(
            SkillActionTypes.ReadScreen,
            $"Captured {screens.Count} screen context item(s).");
    }

    private async Task<SkillActionStepResult> OpenAppAsync(SkillActionStep action)
    {
        var applicationName = !string.IsNullOrWhiteSpace(action.ApplicationName)
            ? action.ApplicationName
            : action.Target;

        if (string.IsNullOrWhiteSpace(applicationName))
        {
            return SkillActionStepResult.Failed(
                action.Type,
                "Open app action requires applicationName.",
                "missing_application_name");
        }

        try
        {
            await _applicationServiceFactory.Create().OpenApplicationAsync(applicationName);
            return SkillActionStepResult.Succeeded(
                SkillActionTypes.OpenApp,
                $"Opened application '{applicationName}'.");
        }
        catch (Exception ex)
        {
            return SkillActionStepResult.Failed(
                SkillActionTypes.OpenApp,
                $"Failed to open application '{applicationName}': {ex.Message}",
                "open_app_failed");
        }
    }

    private static string ResolveTarget(SkillActionStep action)
    {
        if (!string.IsNullOrWhiteSpace(action.Target))
        {
            return action.Target;
        }

        if (!string.IsNullOrWhiteSpace(action.WindowTitle))
        {
            return action.WindowTitle;
        }

        return action.ApplicationName;
    }

    private static string CombineMessage(string planMessage, string stepMessage)
    {
        if (string.IsNullOrWhiteSpace(planMessage))
        {
            return stepMessage;
        }

        if (string.IsNullOrWhiteSpace(stepMessage))
        {
            return planMessage;
        }

        return $"{planMessage} {stepMessage}";
    }
}
