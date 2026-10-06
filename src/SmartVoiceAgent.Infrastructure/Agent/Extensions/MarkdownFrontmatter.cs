namespace SmartVoiceAgent.Infrastructure.Agent.Extensions;

/// <summary>
/// Reads the YAML frontmatter of <c>SKILL.md</c> and command files: <c>key: value</c> pairs, quoted values and
/// folded (<c>&gt;</c>) or literal (<c>|</c>) blocks. Nested structures are kept as their raw text.
/// </summary>
public static class MarkdownFrontmatter
{
    /// <summary>
    /// Splits a document into its frontmatter fields and its body.
    /// </summary>
    /// <param name="text">The Markdown text.</param>
    public static (IReadOnlyDictionary<string, string> Fields, string Body) Parse(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('﻿');
        var lines = normalized.Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return (fields, normalized.Trim());
        }

        var end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
        if (end < 0)
        {
            return (fields, normalized.Trim());
        }

        for (var index = 1; index < end; index++)
        {
            var line = lines[index];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();

            if (value is ">" or ">-" or "|" or "|-" || value.Length == 0)
            {
                var block = new List<string>();
                while (index + 1 < end && (lines[index + 1].Length == 0 || char.IsWhiteSpace(lines[index + 1][0])))
                {
                    block.Add(lines[++index].Trim());
                }

                value = value.StartsWith('|')
                    ? string.Join("\n", block).Trim()
                    : string.Join(" ", block.Where(part => part.Length > 0));
            }
            else if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
            {
                value = value[0] == '"'
                    ? value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal)
                    : value[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }

            fields[key] = value;
        }

        var body = string.Join("\n", lines.Skip(end + 1)).Trim();
        return (fields, body);
    }
}
