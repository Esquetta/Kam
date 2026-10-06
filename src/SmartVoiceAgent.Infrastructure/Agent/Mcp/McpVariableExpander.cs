using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Agent.Mcp;

/// <summary>
/// Resolves references in MCP entries when a server starts: <c>${secret:NAME}</c> from Kam's secret store,
/// <c>${env:NAME}</c> or <c>${NAME}</c> from the environment, and <c>${NAME:-default}</c> with a fallback.
/// </summary>
public static class McpVariableExpander
{
    private static readonly Regex Reference = new(@"\$\{(?<name>[^}:]+(?::[^}:-][^}:]*)?)(?::-(?<fallback>[^}]*))?\}", RegexOptions.Compiled);

    /// <summary>
    /// Replaces every reference in a value. Unknown references become empty, or their fallback.
    /// </summary>
    /// <param name="value">The value from the configuration.</param>
    /// <param name="secretLookup">Returns a saved secret, or <c>null</c>.</param>
    /// <param name="environmentLookup">Returns an environment variable; defaults to the process environment.</param>
    public static string Expand(
        string value,
        Func<string, string?>? secretLookup,
        Func<string, string?>? environmentLookup = null)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains("${", StringComparison.Ordinal))
        {
            return value;
        }

        environmentLookup ??= Environment.GetEnvironmentVariable;
        return Reference.Replace(value, match =>
        {
            var name = match.Groups["name"].Value.Trim();
            string? resolved;
            if (name.StartsWith("secret:", StringComparison.OrdinalIgnoreCase))
            {
                resolved = secretLookup?.Invoke(name["secret:".Length..].Trim());
            }
            else if (name.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            {
                resolved = environmentLookup(name["env:".Length..].Trim());
            }
            else
            {
                resolved = environmentLookup(name);
            }

            return string.IsNullOrEmpty(resolved) && match.Groups["fallback"].Success
                ? match.Groups["fallback"].Value
                : resolved ?? string.Empty;
        });
    }
}
