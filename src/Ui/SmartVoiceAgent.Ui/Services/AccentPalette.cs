using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// The accent colors of one theme variant. <see cref="Accent"/> is for text and icons,
/// <see cref="Strong"/> for fills and focus rings, <see cref="On"/> for text on those fills.
/// </summary>
/// <param name="Accent">Text and icon accent.</param>
/// <param name="Strong">Fill accent, such as the primary button.</param>
/// <param name="StrongHover">Fill accent under the pointer.</param>
/// <param name="On">Text and icons on a <paramref name="Strong"/> fill.</param>
public sealed record AccentColors(Color Accent, Color Strong, Color StrongHover, Color On);

/// <summary>
/// An accent color the user can pick in Settings > Appearance.
/// </summary>
/// <param name="Id">The value saved in settings, such as <c>violet</c>.</param>
/// <param name="Dark">Colors on the dark theme.</param>
/// <param name="Light">Colors on the light theme.</param>
/// <param name="BrandStart">First color of the brand mark gradient.</param>
/// <param name="BrandEnd">Last color of the brand mark gradient.</param>
public sealed record AccentPreset(string Id, AccentColors Dark, AccentColors Light, Color BrandStart, Color BrandEnd)
{
    /// <summary>Gets the preset's name in the interface language.</summary>
    public string Name => Loc.Get($"Settings.Accent.{Id}");
}

/// <summary>
/// The accent presets and the code that swaps them into the app's theme resources.
/// Violet is Kam's original palette and matches <c>Themes/Colors.*.axaml</c>.
/// </summary>
public static class AccentPalette
{
    /// <summary>Opacity of the dark theme's AccentSubtle tint, as <c>Themes/Colors.Dark.axaml</c> uses it.</summary>
    public const byte DarkSubtleAlpha = 0x24;

    /// <summary>Opacity of the light theme's AccentSubtle tint, as <c>Themes/Colors.Light.axaml</c> uses it.</summary>
    public const byte LightSubtleAlpha = 0x1A;

    /// <summary>The preset used when settings name none or an unknown one.</summary>
    public const string DefaultId = "violet";

    private static readonly Color White = Colors.White;

    /// <summary>
    /// Gets the presets in the order Settings shows them.
    /// </summary>
    public static IReadOnlyList<AccentPreset> Presets { get; } =
    [
        new("violet",
            new(Hex("#8B7CFF"), Hex("#6E5BF0"), Hex("#5F4BE6"), White),
            new(Hex("#5B47E0"), Hex("#5B47E0"), Hex("#4A35C9"), White),
            Hex("#8B7CFF"), Hex("#4F46E5")),
        new("blue",
            new(Hex("#7AAEFF"), Hex("#2563EB"), Hex("#1D4ED8"), White),
            new(Hex("#1D5FD6"), Hex("#1D5FD6"), Hex("#174DB0"), White),
            Hex("#7AAEFF"), Hex("#1D4ED8")),
        new("teal",
            new(Hex("#46D3CB"), Hex("#0D7F7A"), Hex("#0B6E6A"), White),
            new(Hex("#0B7570"), Hex("#0B7570"), Hex("#095E5A"), White),
            Hex("#46D3CB"), Hex("#0F766E")),
        new("green",
            new(Hex("#5BE08C"), Hex("#15803D"), Hex("#166534"), White),
            new(Hex("#15803D"), Hex("#15803D"), Hex("#166534"), White),
            Hex("#5BE08C"), Hex("#15803D")),
        new("rose",
            new(Hex("#FB7A93"), Hex("#BE123C"), Hex("#CC1844"), White),
            new(Hex("#BE123C"), Hex("#BE123C"), Hex("#9F1239"), White),
            Hex("#FB7A93"), Hex("#BE123C")),
        new("graphite",
            new(Hex("#D4D4DC"), Hex("#4A4A57"), Hex("#57576A"), White),
            new(Hex("#3F3F4A"), Hex("#3F3F4A"), Hex("#2E2E37"), White),
            Hex("#A3A3B3"), Hex("#3F3F4A")),
    ];

    /// <summary>
    /// Finds a preset by id, falling back to violet.
    /// </summary>
    /// <param name="id">The saved id.</param>
    public static AccentPreset Find(string? id)
    {
        return Presets.FirstOrDefault(preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? Presets[0];
    }

    /// <summary>
    /// Swaps the preset's colors into the app's dark and light theme dictionaries and the brand gradient.
    /// Brushes bind to the colors with DynamicResource, so open windows recolor at once.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <param name="preset">The preset.</param>
    public static void Apply(global::Avalonia.Application app, AccentPreset preset)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(preset);

        SetVariant(app, ThemeVariant.Dark, preset.Dark, DarkSubtleAlpha);
        SetVariant(app, ThemeVariant.Light, preset.Light, LightSubtleAlpha);

        var brand = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
        };
        brand.GradientStops.Add(new GradientStop(preset.BrandStart, 0));
        brand.GradientStops.Add(new GradientStop(preset.BrandEnd, 1));
        app.Resources["BrandGradientBrush"] = brand;
    }

    /// <summary>
    /// Returns the colors a preset writes for one theme variant, keyed like <c>Themes/Colors.*.axaml</c>.
    /// </summary>
    /// <param name="colors">The variant's accent colors.</param>
    /// <param name="subtleAlpha">Opacity of the subtle tint, as the theme files use it.</param>
    public static IReadOnlyDictionary<string, Color> ResourcesFor(AccentColors colors, byte subtleAlpha)
    {
        return new Dictionary<string, Color>
        {
            ["Accent"] = colors.Accent,
            ["AccentStrong"] = colors.Strong,
            ["AccentStrongHover"] = colors.StrongHover,
            ["AccentSubtle"] = Color.FromArgb(subtleAlpha, colors.Accent.R, colors.Accent.G, colors.Accent.B),
            ["AccentOn"] = colors.On,
            // Fluent controls (toggles, sliders, focus) follow the accent too.
            ["SystemAccentColor"] = colors.Strong,
            ["SystemAccentColorLight1"] = Mix(colors.Strong, Colors.White, 0.15),
            ["SystemAccentColorLight2"] = Mix(colors.Strong, Colors.White, 0.30),
            ["SystemAccentColorLight3"] = Mix(colors.Strong, Colors.White, 0.45),
            ["SystemAccentColorDark1"] = Mix(colors.Strong, Colors.Black, 0.12),
            ["SystemAccentColorDark2"] = Mix(colors.Strong, Colors.Black, 0.25),
            ["SystemAccentColorDark3"] = Mix(colors.Strong, Colors.Black, 0.38)
        };
    }

    private static void SetVariant(global::Avalonia.Application app, ThemeVariant variant, AccentColors colors, byte subtleAlpha)
    {
        // The theme files live in a merged dictionary; theme dictionaries on Application.Resources itself
        // are looked up before merged ones, so the preset wins without editing the files.
        if (!app.Resources.ThemeDictionaries.TryGetValue(variant, out var provider)
            || provider is not IResourceDictionary dictionary)
        {
            var created = new ResourceDictionary();
            app.Resources.ThemeDictionaries[variant] = created;
            dictionary = created;
        }

        foreach (var (key, color) in ResourcesFor(colors, subtleAlpha))
        {
            dictionary[key] = color;
        }
    }

    private static Color Mix(Color color, Color with, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + ((b - a) * amount));
        return Color.FromRgb(Channel(color.R, with.R), Channel(color.G, with.G), Channel(color.B, with.B));
    }

    private static Color Hex(string value) => Color.Parse(value);
}
