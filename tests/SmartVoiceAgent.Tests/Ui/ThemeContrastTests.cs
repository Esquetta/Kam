using Avalonia.Media;
using FluentAssertions;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class ThemeContrastTests
{
    [Theory]
    [InlineData("Colors.Light.axaml")]
    [InlineData("Colors.Dark.axaml")]
    public void ThemePalette_ProvidesReadableCoreTextContrast(string themeFileName)
    {
        var colors = LoadThemeColors(themeFileName);

        Contrast(colors["TextPrimary"], colors["PanelBg"]).Should().BeGreaterThanOrEqualTo(7.0);
        Contrast(colors["TextPrimary"], colors["CardBg"]).Should().BeGreaterThanOrEqualTo(7.0);
        Contrast(colors["TextSecondary"], colors["PanelBg"]).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Theory]
    [InlineData("Colors.Light.axaml")]
    [InlineData("Colors.Dark.axaml")]
    public void ThemePalette_ProvidesReadableAccentButtonContrast(string themeFileName)
    {
        var colors = LoadThemeColors(themeFileName);

        Contrast(colors["AccentGreenOn"], colors["AccentGreen"]).Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(colors["AccentErrorOn"], colors["AccentError"]).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Theory]
    [InlineData("Colors.Light.axaml")]
    [InlineData("Colors.Dark.axaml")]
    public void ThemePalette_ProvidesReadableMutedTextContrastOnCards(string themeFileName)
    {
        var colors = LoadThemeColors(themeFileName);

        Contrast(colors["TextMuted"], colors["CardBg"]).Should().BeGreaterThanOrEqualTo(4.5);
        Contrast(colors["TextMuted"], colors["SurfaceBg"]).Should().BeGreaterThanOrEqualTo(4.5);
    }

    private static Dictionary<string, Color> LoadThemeColors(string themeFileName)
    {
        var document = XDocument.Load(FindThemePath(themeFileName));
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(
                element => element.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Value,
                element => Color.Parse(element.Value));
    }

    private static string FindThemePath(string themeFileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Ui",
                "SmartVoiceAgent.Ui",
                "Themes",
                themeFileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {themeFileName} from the test output directory.");
    }

    private static double Contrast(Color foreground, Color background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255.0;
            return normalized <= 0.03928
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R)
            + 0.7152 * Channel(color.G)
            + 0.0722 * Channel(color.B);
    }
}
