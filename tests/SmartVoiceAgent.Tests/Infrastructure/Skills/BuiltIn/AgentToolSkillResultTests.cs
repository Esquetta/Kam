using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public sealed class AgentToolSkillResultTests
{
    [Fact]
    public void FromMessage_WithFailureTextInsideClipboardContent_ReturnsSuccess()
    {
        var result = AgentToolSkillResult.FromMessage(
            "Clipboard content:\n```\nFatal error! Unhandled exception\n```");

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Fatal error");
    }

    [Theory]
    [InlineData("Dosya başarıyla okundu:\n\n2026-10-01 [Error] Build failed: could not find file")]
    [InlineData("Search results for 'error CS1002 failed build' (2):\n- Fix error CS1002\n  URL: https://example.com")]
    [InlineData("No results found for 'unavailable service'.")]
    [InlineData("Spotify başarıyla başlatıldı.")]
    [InlineData("✅ Email sent successfully to a@b.c. Subject: Build failed")]
    [InlineData("🔋 Battery Status:\n```\nError reading cycle count\n```")]
    public void FromMessage_WithFailureWordsOnlyInContentOrQuotes_ReturnsSuccess(string message)
    {
        AgentToolSkillResult.FromMessage(message).Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("Hata: 'C:\\notes.txt' dosyası bulunamadı.")]
    [InlineData("Dosya okuma hatası: Access denied")]
    [InlineData("Spotify açılamadı. Hata: not installed")]
    [InlineData("❌ SMS service is not configured.")]
    [InlineData("Web search could not be completed. Error: timeout")]
    [InlineData("Güvenlik uyarısı: '.exe' dosyaları otomatik olarak açılamaz.")]
    [InlineData("Durum kontrolü başarısız: denied")]
    public void FromMessage_WithFailureOnFirstLine_ReturnsFailure(string message)
    {
        AgentToolSkillResult.FromMessage(message).Success.Should().BeFalse();
    }
}
