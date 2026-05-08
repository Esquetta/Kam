using FluentAssertions;
using SmartVoiceAgent.Mailing.Security;

namespace SmartVoiceAgent.Tests.Mailing.Security;

public sealed class MailingLogSanitizerTests
{
    [Theory]
    [InlineData("test@example.com", "t***@example.com")]
    [InlineData("a@example.com", "a***@example.com")]
    [InlineData("not-an-email", "[redacted]")]
    public void MaskEmail_RedactsLocalPart(string input, string expected)
    {
        MailingLogSanitizer.MaskEmail(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("+15551234567", "+*******4567")]
    [InlineData("5551234567", "******4567")]
    [InlineData("1234", "[redacted]")]
    public void MaskPhoneNumber_LeavesOnlyDialingPrefixAndSuffix(string input, string expected)
    {
        MailingLogSanitizer.MaskPhoneNumber(input).Should().Be(expected);
    }

    [Fact]
    public void MaskIdentifier_LeavesOnlySuffix()
    {
        MailingLogSanitizer.MaskIdentifier("AC1234567890abcdef").Should().Be("**************cdef");
    }
}
