using Avalonia.Media;
using FluentAssertions;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class AccentPaletteTests
{
    // Card backgrounds from Themes/Colors.Dark.axaml and Colors.Light.axaml, where accent text sits most.
    private static readonly Color DarkCard = Color.Parse("#16161C");
    private static readonly Color LightCard = Color.Parse("#FFFFFF");

    public static TheoryData<string> PresetIds()
    {
        var data = new TheoryData<string>();
        foreach (var preset in AccentPalette.Presets)
        {
            data.Add(preset.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PresetIds))]
    public void Preset_KeepsButtonTextReadable(string id)
    {
        var preset = AccentPalette.Find(id);

        foreach (var colors in new[] { preset.Dark, preset.Light })
        {
            Contrast(colors.On, colors.Strong).Should().BeGreaterThanOrEqualTo(4.5, $"{id}: text on a primary button");
            Contrast(colors.On, colors.StrongHover).Should().BeGreaterThanOrEqualTo(4.5, $"{id}: text on a hovered primary button");
        }
    }

    [Theory]
    [MemberData(nameof(PresetIds))]
    public void Preset_KeepsAccentTextReadableOnCards(string id)
    {
        var preset = AccentPalette.Find(id);

        Contrast(preset.Dark.Accent, DarkCard).Should().BeGreaterThanOrEqualTo(4.5, $"{id}: accent text on a dark card");
        Contrast(preset.Light.Accent, LightCard).Should().BeGreaterThanOrEqualTo(4.5, $"{id}: accent text on a light card");
    }

    [Fact]
    public void Violet_MatchesTheThemeFiles()
    {
        var violet = AccentPalette.Find("violet");

        violet.Dark.Accent.Should().Be(Color.Parse("#8B7CFF"));
        violet.Dark.Strong.Should().Be(Color.Parse("#6E5BF0"));
        violet.Light.Accent.Should().Be(Color.Parse("#5B47E0"));
        foreach (var (file, colors, alpha) in new[]
        {
            ("Colors.Dark.axaml", violet.Dark, AccentPalette.DarkSubtleAlpha),
            ("Colors.Light.axaml", violet.Light, AccentPalette.LightSubtleAlpha)
        })
        {
            var theme = ThemeColor(file);
            var written = AccentPalette.ResourcesFor(colors, alpha);
            foreach (var key in new[] { "Accent", "AccentStrong", "AccentSubtle" })
            {
                written[key].Should().Be(theme(key), $"violet's {key} in {file} is what the app starts with");
            }
        }
    }

    [Fact]
    public void Find_FallsBackToViolet()
    {
        AccentPalette.Find(null).Id.Should().Be("violet");
        AccentPalette.Find("chartreuse").Id.Should().Be("violet");
        AccentPalette.Find("TEAL").Id.Should().Be("teal");
    }

    private static Func<string, Color> ThemeColor(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "src", "Ui", "SmartVoiceAgent.Ui", "Themes", fileName)))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(directory!.FullName, "src", "Ui", "SmartVoiceAgent.Ui", "Themes", fileName);
        var colors = System.Xml.Linq.XDocument.Load(path).Root!.Elements()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(
                element => element.Attributes().First(attribute => attribute.Name.LocalName == "Key").Value,
                element => Color.Parse(element.Value.Trim()));
        return key => colors[key];
    }

    private static double Contrast(Color foreground, Color background)
    {
        var lighter = Math.Max(Luminance(foreground), Luminance(background));
        var darker = Math.Min(Luminance(foreground), Luminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255.0;
            return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }
}
