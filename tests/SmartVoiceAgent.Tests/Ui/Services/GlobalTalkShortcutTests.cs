using FluentAssertions;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class GlobalTalkShortcutTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", 0x0003u, 0x20u)]
    [InlineData("Ctrl+Shift+Space", 0x0006u, 0x20u)]
    [InlineData("Ctrl+Alt+K", 0x0003u, 0x4Bu)]
    [InlineData("Pause", 0x0000u, 0x13u)]
    [InlineData("Win+F9", 0x0008u, 0x78u)]
    [InlineData("Alt+D5", 0x0001u, 0x35u)]
    public void TryParse_MapsShortcutsToWindowsHotkeys(string shortcut, uint modifiers, uint virtualKey)
    {
        GlobalTalkShortcut.TryParse(shortcut, out var parsedModifiers, out var parsedKey).Should().BeTrue();

        parsedModifiers.Should().Be(modifiers);
        parsedKey.Should().Be(virtualKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt+NotAKey")]
    [InlineData("Ctrl+Enter")]
    public void TryParse_RejectsEmptyInvalidAndUnsupportedShortcuts(string? shortcut)
    {
        GlobalTalkShortcut.TryParse(shortcut, out _, out _).Should().BeFalse();
    }
}
