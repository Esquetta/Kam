namespace SmartVoiceAgent.Mailing.Security;

public static class MailingLogSanitizer
{
    public const string Redacted = "[redacted]";

    public static string MaskEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var separatorIndex = trimmed.IndexOf('@', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == trimmed.Length - 1)
        {
            return Redacted;
        }

        var localPart = trimmed[..separatorIndex];
        var domain = trimmed[(separatorIndex + 1)..];
        return $"{localPart[0]}***@{domain}";
    }

    public static string MaskEmails(IEnumerable<string> values)
    {
        return string.Join(", ", values.Select(MaskEmail));
    }

    public static string MaskPhoneNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length <= 4)
        {
            return Redacted;
        }

        var prefix = trimmed.StartsWith("+", StringComparison.Ordinal) ? "+" : string.Empty;
        return $"{prefix}{new string('*', Math.Max(4, digits.Length - 4))}{digits[^4..]}";
    }

    public static string MaskIdentifier(string? value, int visibleSuffixLength = 4)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= visibleSuffixLength)
        {
            return Redacted;
        }

        return $"{new string('*', Math.Max(4, trimmed.Length - visibleSuffixLength))}{trimmed[^visibleSuffixLength..]}";
    }
}
