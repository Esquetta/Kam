using System.Text;

namespace SmartVoiceAgent.Infrastructure.Skills.Adapters;

internal static class SkillIdNormalizer
{
    public static string Normalize(string value)
    {
        var builder = new StringBuilder();

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            {
                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character))
            {
                builder.Append('-');
            }
        }

        return builder.Length == 0 ? "skill" : builder.ToString();
    }
}
