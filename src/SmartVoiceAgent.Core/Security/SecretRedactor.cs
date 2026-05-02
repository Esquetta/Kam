using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Core.Security;

public static partial class SecretRedactor
{
    public const string Replacement = "[redacted]";

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = OpenAiKeyPattern().Replace(value, Replacement);
        redacted = BearerTokenPattern().Replace(redacted, Replacement);
        redacted = KeyValueSecretPattern().Replace(redacted, match =>
            $"{match.Groups["name"].Value}={Replacement}");
        return redacted;
    }

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_\-]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpenAiKeyPattern();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._\-]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(
        @"(?<name>(?:api[_-]?key|password|secret|token))=(?<value>[^&\s,""'}]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecretPattern();
}
