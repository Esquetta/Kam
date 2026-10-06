using System.Text.RegularExpressions;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

/// <summary>
/// Turns the text an agent tool returns into a skill result.
/// </summary>
public static class AgentToolSkillResult
{
    private static readonly string[] SuccessPrefixes = ["✅", "📋", "🔋"];

    private static readonly string[] FailurePrefixes = ["❌", "Hata", "Error", "Failed", "Cannot ", "Güvenlik"];

    private static readonly string[] FailureWords =
    [
        "hata:", "hatası", "hata oluştu", "error:", "failed", "başarısız", "alınamadı", "açılamadı",
        "çalınamadı", "edilemedi", "could not", "reddedildi", "not configured", "not supported",
        "not available", "unavailable"
    ];

    // Tools quote the names and queries they echo back, so quoted text never decides the outcome.
    private static readonly Regex QuotedText = new("'[^']*'|\"[^\"]*\"|`[^`]*`", RegexOptions.Compiled);

    /// <summary>
    /// Builds a result from a tool's reply. Tools state the outcome on the first line and put
    /// file contents, search results or clipboard text after it, so only the first line decides;
    /// a file that mentions "error" is still a successful read.
    /// </summary>
    /// <param name="message">The tool's reply.</param>
    /// <returns>A failed result when the first line reports a failure, otherwise a successful one.</returns>
    public static SkillResult FromMessage(string message)
    {
        return LooksLikeFailure(message)
            ? SkillResult.Failed(message)
            : SkillResult.Succeeded(message);
    }

    private static bool LooksLikeFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var status = message.TrimStart();
        var lineEnd = status.IndexOfAny(['\r', '\n']);
        if (lineEnd >= 0)
        {
            status = status[..lineEnd];
        }

        if (SuccessPrefixes.Any(prefix => status.StartsWith(prefix, StringComparison.Ordinal)))
        {
            return false;
        }

        if (FailurePrefixes.Any(prefix => status.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        status = QuotedText.Replace(status, "''");
        return FailureWords.Any(word => status.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
