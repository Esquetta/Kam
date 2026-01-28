using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Infrastructure.Security;

/// <summary>
/// Security utilities for input validation and sanitization
/// </summary>
public static class SecurityUtilities
{
    // Patterns that could indicate path traversal attempts
    private static readonly string[] PathTraversalPatterns = new[]
    {
        "..", "//", "\\\\", "%2e%2e", "%2f", "%5c", "0x2e0x2e"
    };

    // Characters not allowed in file names
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
    private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

    // Allowed safe characters for application names
    private static readonly Regex SafeAppNameRegex = new(@"^[\w\s\-\.]+$", RegexOptions.Compiled);

    // Maximum path length to prevent buffer overflow attempts
    private const int MaxPathLength = 260;

    /// <summary>
    /// Validates a file path to prevent path traversal attacks
    /// </summary>
    /// <param name="path">The path to validate</param>
    /// <param name="allowedBaseDirectory">Optional base directory that the path must be within</param>
    /// <returns>True if the path is safe, false otherwise</returns>
    public static bool IsSafeFilePath(string path, string? allowedBaseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Check path length
        if (path.Length > MaxPathLength)
            return false;

        // Normalize the path
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        // Check for path traversal patterns in the original path
        foreach (var pattern in PathTraversalPatterns)
        {
            if (path.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // If a base directory is specified, verify the path is within it
        if (!string.IsNullOrEmpty(allowedBaseDirectory))
        {
            string normalizedBase;
            try
            {
                normalizedBase = Path.GetFullPath(allowedBaseDirectory);
            }
            catch
            {
                return false;
            }

            // Ensure the path starts with the base directory
            if (!normalizedPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Check for invalid characters
        if (path.Any(c => InvalidPathChars.Contains(c) || c < 32))
            return false;

        return true;
    }

    /// <summary>
    /// Sanitizes a filename to remove dangerous characters
    /// </summary>
    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed";

        // Remove path traversal attempts
        fileName = fileName.Replace("..", "");
        fileName = fileName.Replace("/", "");
        fileName = fileName.Replace("\\", "");

        // Remove invalid characters
        foreach (var c in InvalidFileNameChars)
        {
            fileName = fileName.Replace(c.ToString(), "");
        }

        // Trim and limit length
        fileName = fileName.Trim();
        if (fileName.Length > 100)
            fileName = fileName.Substring(0, 100);

        return string.IsNullOrEmpty(fileName) ? "unnamed" : fileName;
    }

    /// <summary>
    /// Validates an application name to prevent command injection
    /// </summary>
    public static bool IsSafeApplicationName(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
            return false;

        // Check length
        if (appName.Length > 100)
            return false;

        // Check for shell injection characters
        var dangerousChars = new[] { ';', '|', '&', '>', '<', '`', '$', '(', ')', '{', '}', '[', ']', '!', '\\', '/' };
        if (appName.Any(c => dangerousChars.Contains(c)))
            return false;

        // Check for command sequences
        var dangerousPatterns = new[] { "&&", "||", "|", ";", "$(", "`", ">>", ">", "<" };
        foreach (var pattern in dangerousPatterns)
        {
            if (appName.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Must match safe pattern (alphanumeric, spaces, hyphens, dots)
        return SafeAppNameRegex.IsMatch(appName);
    }

    /// <summary>
    /// Sanitizes a URL to prevent open redirect and injection attacks
    /// </summary>
    public static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Only allow http and https protocols
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return false;

        // Check for JavaScript protocol injection
        var lowerUrl = url.ToLowerInvariant();
        if (lowerUrl.StartsWith("javascript:") ||
            lowerUrl.StartsWith("data:") ||
            lowerUrl.StartsWith("vbscript:") ||
            lowerUrl.StartsWith("file:") ||
            lowerUrl.Contains("javascript%3a") ||
            lowerUrl.Contains("data%3a"))
            return false;

        // Check for control characters
        if (url.Any(c => c < 32))
            return false;

        // Check URL length
        if (url.Length > 2048)
            return false;

        return true;
    }

    /// <summary>
    /// Masks sensitive data in strings for logging purposes
    /// </summary>
    public static string MaskSensitiveData(string input, int visibleChars = 4)
    {
        if (string.IsNullOrEmpty(input))
            return "***";

        if (input.Length <= visibleChars * 2)
            return "***";

        return input.Substring(0, visibleChars) + 
               new string('*', input.Length - visibleChars * 2) + 
               input.Substring(input.Length - visibleChars);
    }

    /// <summary>
    /// Validates that a string contains only safe characters for logging
    /// </summary>
    public static string SanitizeForLog(string input, int maxLength = 1000)
    {
        if (string.IsNullOrEmpty(input))
            return "";

        // Remove newlines and control characters
        input = input.Replace("\r", "").Replace("\n", " | ");
        input = new string(input.Where(c => c >= 32 || c == ' ').ToArray());

        // Limit length
        if (input.Length > maxLength)
            input = input.Substring(0, maxLength) + "...";

        return input;
    }

    /// <summary>
    /// Checks if a file extension is potentially dangerous
    /// </summary>
    public static bool IsDangerousExtension(string extension)
    {
        var dangerousExtensions = new[]
        {
            ".exe", ".bat", ".cmd", ".sh", ".msi", ".dll", ".com",
            ".scr", ".pif", ".vbs", ".js", ".wsf", ".ps1"
        };

        return dangerousExtensions.Contains(extension.ToLowerInvariant());
    }

    /// <summary>
    /// Validates directory path is within allowed base directories
    /// </summary>
    public static bool IsPathWithinAllowedDirectories(string path, IEnumerable<string> allowedDirectories)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        foreach (var allowedDir in allowedDirectories)
        {
            string normalizedAllowed;
            try
            {
                normalizedAllowed = Path.GetFullPath(allowedDir);
            }
            catch
            {
                continue;
            }

            if (normalizedPath.StartsWith(normalizedAllowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
