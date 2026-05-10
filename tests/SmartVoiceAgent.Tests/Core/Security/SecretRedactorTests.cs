using FluentAssertions;
using SmartVoiceAgent.Core.Security;

namespace SmartVoiceAgent.Tests.Core.Security;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Redact_RemovesJsonSecretValues()
    {
        var input = """
            {"apiKey":"sk-live-secret","smtpPassword":"smtp-password","note":"safe"}
            """;

        var redacted = SecretRedactor.Redact(input);

        redacted.Should().Contain("\"apiKey\":\"[redacted]\"");
        redacted.Should().Contain("\"smtpPassword\":\"[redacted]\"");
        redacted.Should().Contain("\"note\":\"safe\"");
        redacted.Should().NotContain("sk-live-secret");
        redacted.Should().NotContain("smtp-password");
    }

    [Fact]
    public void Redact_RemovesPrivateKeyBlocks()
    {
        var input = """
            before
            -----BEGIN RSA PRIVATE KEY-----
            abc123
            -----END RSA PRIVATE KEY-----
            after
            """;

        var redacted = SecretRedactor.Redact(input);

        redacted.Should().Contain("before");
        redacted.Should().Contain("after");
        redacted.Should().Contain(SecretRedactor.Replacement);
        redacted.Should().NotContain("abc123");
        redacted.Should().NotContain("BEGIN RSA PRIVATE KEY");
    }

    [Fact]
    public void Redact_RemovesGitHubTokens()
    {
        var input = "token=ghp_1234567890abcdefghijklmnopqrstuvwxyz";

        var redacted = SecretRedactor.Redact(input);

        redacted.Should().Contain("token=[redacted]");
        redacted.Should().NotContain("ghp_1234567890abcdefghijklmnopqrstuvwxyz");
    }
}
