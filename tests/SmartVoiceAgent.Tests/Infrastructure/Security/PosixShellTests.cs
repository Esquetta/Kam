using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Security;

namespace SmartVoiceAgent.Tests.Infrastructure.Security;

public sealed class PosixShellTests
{
    [Theory]
    [InlineData("firefox", "'firefox'")]
    [InlineData("Visual Studio Code", "'Visual Studio Code'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("", "''")]
    public void Quote_WrapsValueAsSingleShellWord(string value, string expected)
    {
        PosixShell.Quote(value).Should().Be(expected);
    }

    [Fact]
    public void Run_WithQuotedPayload_DoesNotExecuteInjectedCommand()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/bash"))
        {
            return;
        }

        var marker = Path.Combine(Path.GetTempPath(), $"kam-shell-{Guid.NewGuid():N}");
        var payload = $"x; touch {marker}; echo '$(touch {marker})'";

        var output = PosixShell.Run("/bin/bash", $"printf %s {PosixShell.Quote(payload)}");

        output.Should().Be(payload);
        File.Exists(marker).Should().BeFalse();
    }
}
