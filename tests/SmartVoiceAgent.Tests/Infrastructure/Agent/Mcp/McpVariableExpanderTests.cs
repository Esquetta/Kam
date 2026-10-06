using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Agent.Mcp;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Mcp;

public class McpVariableExpanderTests
{
    private static readonly Dictionary<string, string> Environment = new() { ["HOME"] = "/home/me", ["EMPTY"] = "" };

    [Theory]
    [InlineData("${HOME}/docs", "/home/me/docs")]
    [InlineData("${env:HOME}/docs", "/home/me/docs")]
    [InlineData("Bearer ${secret:token}", "Bearer s3cret")]
    [InlineData("${MISSING}", "")]
    [InlineData("${MISSING:-fallback}", "fallback")]
    [InlineData("${EMPTY:-fallback}", "fallback")]
    [InlineData("${secret:missing:-none}", "none")]
    [InlineData("plain value", "plain value")]
    [InlineData("${HOME}:${secret:token}", "/home/me:s3cret")]
    public void Expand_ResolvesReferences(string value, string expected)
    {
        var result = McpVariableExpander.Expand(
            value,
            name => name == "token" ? "s3cret" : null,
            name => Environment.TryGetValue(name, out var found) ? found : null);

        result.Should().Be(expected);
    }

    [Fact]
    public void Expand_WithoutSecretLookup_LeavesSecretsEmpty()
    {
        McpVariableExpander.Expand("x${secret:token}y", null, _ => null).Should().Be("xy");
    }
}
