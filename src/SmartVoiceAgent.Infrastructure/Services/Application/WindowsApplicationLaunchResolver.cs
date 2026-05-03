using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Services.Application;

[SupportedOSPlatform("windows")]
public static class WindowsApplicationLaunchResolver
{
    private static readonly Regex ProtocolRegex = new(@"^[a-z][a-z0-9+.-]{1,63}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? CreateRegisteredProtocolUri(
        string appName,
        IEnumerable<string> registeredProtocols)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return null;
        }

        var candidates = CreateProtocolCandidates(appName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var protocol = registeredProtocols
            .Where(IsSafeProtocolName)
            .FirstOrDefault(candidates.Contains);

        return protocol is null
            ? null
            : $"{protocol.ToLowerInvariant()}:";
    }

    public static string? FindRegisteredProtocolUri(string appName)
    {
        var protocols = CreateProtocolCandidates(appName)
            .Where(IsRegisteredUriProtocol)
            .ToArray();

        return CreateRegisteredProtocolUri(appName, protocols);
    }

    public static bool IsWindowsAppsExecutionAlias(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && path.Contains(@"\Microsoft\WindowsApps\", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> CreateProtocolCandidates(string appName)
    {
        var normalized = NormalizeProtocolCandidate(appName);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            yield return normalized;
        }

        var compact = NormalizeProtocolCandidate(
            appName.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Replace(".", string.Empty, StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(compact)
            && !compact.Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            yield return compact;
        }
    }

    private static string NormalizeProtocolCandidate(string value)
    {
        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character) || character is '+' or '.' or '-')
            .ToArray());
    }

    private static bool IsRegisteredUriProtocol(string protocol)
    {
        if (!IsSafeProtocolName(protocol))
        {
            return false;
        }

        return HasUrlProtocolValue(Registry.CurrentUser, $@"Software\Classes\{protocol}")
            || HasUrlProtocolValue(Registry.ClassesRoot, protocol);
    }

    private static bool HasUrlProtocolValue(RegistryKey baseKey, string subKeyPath)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath);
            return key?.GetValue("URL Protocol") is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSafeProtocolName(string protocol)
    {
        return !string.IsNullOrWhiteSpace(protocol)
            && ProtocolRegex.IsMatch(protocol);
    }
}
