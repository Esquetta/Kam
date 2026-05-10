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

        var redacted = PemPrivateKeyPattern().Replace(value, Replacement);
        redacted = JsonSecretPattern().Replace(redacted, match =>
            $"{match.Groups["prefix"].Value}{Replacement}{match.Groups["suffix"].Value}");
        redacted = OpenAiKeyPattern().Replace(redacted, Replacement);
        redacted = GitHubTokenPattern().Replace(redacted, Replacement);
        redacted = BearerTokenPattern().Replace(redacted, Replacement);
        redacted = KeyValueSecretPattern().Replace(redacted, match =>
            $"{match.Groups["name"].Value}={Replacement}");
        return redacted;
    }

    [GeneratedRegex(
        @"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----.*?-----END [A-Z0-9 ]*PRIVATE KEY-----",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PemPrivateKeyPattern();

    [GeneratedRegex(
        @"(?<prefix>[""'](?<name>(?:api[_-]?key|app[_-]?password|auth[_-]?token|client[_-]?secret|password|private[_-]?key|secret|smtp[_-]?password|todoist[_-]?api[_-]?key|token|twilio[_-]?auth[_-]?token))[""']\s*:\s*[""'])(?<value>[^""']+)(?<suffix>[""'])",
        RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecretPattern();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_\-]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpenAiKeyPattern();

    [GeneratedRegex(@"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{20,}\b|\bgithub_pat_[A-Za-z0-9_]{20,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubTokenPattern();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._\-]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex BearerTokenPattern();

    [GeneratedRegex(
        @"(?<name>(?:api[_-]?key|password|secret|token))=(?<value>[^&\s,""'}]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecretPattern();
}
