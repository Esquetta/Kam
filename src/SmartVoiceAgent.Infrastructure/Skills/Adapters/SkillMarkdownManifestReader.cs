using System.Security.Cryptography;
using SmartVoiceAgent.Core.Models.Skills;

namespace SmartVoiceAgent.Infrastructure.Skills.Adapters;

internal static class SkillMarkdownManifestReader
{
    public static SkillMarkdownManifestMetadata? Read(string skillDirectory)
    {
        var skillFile = ResolveManifestFile(skillDirectory);
        if (!File.Exists(skillFile))
        {
            return null;
        }

        var lines = File.ReadAllLines(skillFile);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            for (var index = 1; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (line == "---")
                {
                    break;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim().Trim('"');
                metadata[key] = value;
            }
        }

        var fallbackName = Path.GetFileName(skillDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var name = metadata.TryGetValue("name", out var metadataName) && !string.IsNullOrWhiteSpace(metadataName)
            ? metadataName
            : ReadFirstHeading(lines) ?? fallbackName;
        var description = metadata.TryGetValue("description", out var metadataDescription)
            ? metadataDescription
            : ReadFirstParagraph(lines);
        var permissions = ReadPermissions(metadata);

        return string.IsNullOrWhiteSpace(name)
            ? null
            : new SkillMarkdownManifestMetadata(
                name,
                description,
                ComputeChecksum(skillFile),
                skillDirectory,
                DateTimeOffset.UtcNow,
                permissions.Count == 0 ? [SkillPermission.None] : permissions,
                DetermineRiskLevel(permissions));
    }

    private static string ResolveManifestFile(string skillDirectory)
    {
        foreach (var fileName in new[] { "SKILL.md", "skill.md", "README.md", "README.txt" })
        {
            var candidate = Path.Combine(skillDirectory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(skillDirectory, "SKILL.md");
    }

    private static string? ReadFirstHeading(IReadOnlyList<string> lines)
    {
        var heading = lines
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal));

        return heading is null ? null : heading[2..].Trim();
    }

    private static string ReadFirstParagraph(IReadOnlyList<string> lines)
    {
        var inFrontMatter = lines.Count > 0 && lines[0].Trim() == "---";
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (inFrontMatter)
            {
                if (index > 0 && line == "---")
                {
                    inFrontMatter = false;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            return line;
        }

        return string.Empty;
    }

    private static List<SkillPermission> ReadPermissions(IReadOnlyDictionary<string, string> metadata)
    {
        var values = new List<string>();
        foreach (var key in new[] { "allowed-tools", "allowed_tools", "tools", "permissions" })
        {
            if (metadata.TryGetValue(key, out var rawValue) && !string.IsNullOrWhiteSpace(rawValue))
            {
                values.AddRange(SplitMetadataList(rawValue));
            }
        }

        return values
            .Select(MapPermission)
            .Where(permission => permission.HasValue)
            .Select(permission => permission!.Value)
            .Distinct()
            .ToList();
    }

    private static IEnumerable<string> SplitMetadataList(string value)
    {
        return value
            .Trim()
            .Trim('[', ']')
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().Trim('"', '\'', '`'));
    }

    private static SkillPermission? MapPermission(string token)
    {
        var normalized = token
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "read" or "file" or "fileread" or "filesystemread" => SkillPermission.FileSystemRead,
            "write" or "edit" or "multiedit" or "filewrite" or "filesystemwrite" => SkillPermission.FileSystemWrite,
            "bash" or "shell" or "powershell" or "terminal" or "process" or "processlaunch" => SkillPermission.ProcessLaunch,
            "webfetch" or "websearch" or "fetch" or "network" or "http" => SkillPermission.Network,
            "clipboard" or "clipboardread" or "getclipboard" => SkillPermission.ClipboardRead,
            "clipboardwrite" or "setclipboard" => SkillPermission.ClipboardWrite,
            "system" or "systeminformation" => SkillPermission.SystemInformation,
            _ => null
        };
    }

    private static SkillRiskLevel DetermineRiskLevel(IReadOnlyCollection<SkillPermission> permissions)
    {
        if (permissions.Count == 0)
        {
            return SkillRiskLevel.Medium;
        }

        if (permissions.Contains(SkillPermission.FileSystemWrite)
            || permissions.Contains(SkillPermission.ProcessLaunch)
            || permissions.Contains(SkillPermission.ProcessControl))
        {
            return SkillRiskLevel.High;
        }

        if (permissions.Contains(SkillPermission.Network)
            || permissions.Contains(SkillPermission.ClipboardWrite))
        {
            return SkillRiskLevel.Medium;
        }

        return SkillRiskLevel.Low;
    }

    private static string ComputeChecksum(string skillFile)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(skillFile);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

internal sealed record SkillMarkdownManifestMetadata(
    string Name,
    string Description,
    string Checksum,
    string InstalledFrom,
    DateTimeOffset InstalledAt,
    IReadOnlyCollection<SkillPermission> Permissions,
    SkillRiskLevel RiskLevel);
