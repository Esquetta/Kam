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
        AccentPalette.ResourcesFor(violet.Dark, 0x24)["AccentSubtle"].Should().Be(Color.Parse("#248B7CFF"));
    }

    [Fact]
    public void Find_FallsBackToViolet()
    {
        AccentPalette.Find(null).Id.Should().Be("violet");
        AccentPalette.Find("chartreuse").Id.Should().Be("violet");
        AccentPalette.Find("TEAL").Id.Should().Be("teal");
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
