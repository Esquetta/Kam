using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Core.Interfaces;

public interface IDesktopAutomationAdapter
{
    Task<SkillActionStepResult> ClickAsync(
        int x,
        int y,
        CancellationToken cancellationToken = default);

    Task<SkillActionStepResult> TypeTextAsync(
        string text,
        CancellationToken cancellationToken = default);

    Task<SkillActionStepResult> HotkeyAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default);

    Task<SkillActionStepResult> FocusWindowAsync(
        string target,
        CancellationToken cancellationToken = default);
}
