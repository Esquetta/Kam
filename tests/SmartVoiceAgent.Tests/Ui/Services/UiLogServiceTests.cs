using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Ui.Services.Concrete;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class UiLogServiceTests
{
    [Fact]
    public void Log_RedactsSecretsBeforeRaisingUiEvent()
    {
        var service = new UiLogService();
        UiLogEntry? captured = null;
        service.OnLogEntry += (_, entry) => captured = entry;

        service.Log(
            "Failed with sk-test-secret Bearer abc123 password=secret api_key=secret",
            LogLevel.Error,
            "source api_key=secret");

        captured.Should().NotBeNull();
        captured!.Message.Should().NotContain("sk-test-secret");
        captured.Message.Should().NotContain("Bearer abc123");
        captured.Message.Should().NotContain("password=secret");
        captured.Message.Should().NotContain("api_key=secret");
        captured.Message.Should().Contain("[redacted]");
        captured.Source.Should().NotContain("api_key=secret");
    }
}
