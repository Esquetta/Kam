namespace SmartVoiceAgent.Infrastructure.Skills.Adapters;

internal static class SkillMarkdownManifestReader
{
    public static (string Name, string Description)? Read(string skillDirectory)
    {
        var skillFile = Path.Combine(skillDirectory, "SKILL.md");
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
            : fallbackName;
        var description = metadata.TryGetValue("description", out var metadataDescription)
            ? metadataDescription
            : string.Empty;

        return string.IsNullOrWhiteSpace(name)
            ? null
            : (name, description);
    }
}
