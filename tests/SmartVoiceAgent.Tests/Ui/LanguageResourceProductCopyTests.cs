using FluentAssertions;
using SmartVoiceAgent.Ui.Services;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Tests.Ui;

public sealed partial class LanguageResourceProductCopyTests
{
    [Fact]
    public void LanguageFiles_ExistOnlyForSupportedLanguages()
    {
        var codes = Directory.GetFiles(FindLanguageResourceDirectory(), "*.json")
            .Select(file => Path.GetFileName(file).Split('.')[0])
            .Distinct()
            .Order();

        codes.Should().Equal(LocalizationService.SupportedLanguages.Select(language => language.Code).Order());
    }

    [Fact]
    public void TurkishFiles_TranslateEveryEnglishKey()
    {
        var directory = FindLanguageResourceDirectory();
        foreach (var englishFile in Directory.GetFiles(directory, "en-US.*.json"))
        {
            var area = Path.GetFileName(englishFile)["en-US.".Length..];
            var turkishFile = Path.Combine(directory, "tr-TR." + area);
            File.Exists(turkishFile).Should().BeTrue($"{area} needs a Turkish file");

            var english = Load(englishFile);
            var turkish = Load(turkishFile);
            turkish.Keys.Should().BeEquivalentTo(english.Keys, area);

            foreach (var (key, text) in english)
            {
                Placeholders(turkish[key]).Should().BeEquivalentTo(Placeholders(text), $"{area} {key}");
                turkish[key].Should().NotBeNullOrWhiteSpace($"{area} {key}");
            }
        }
    }

    [Fact]
    public void LanguageFiles_DoNotRepeatKeysAcrossAreas()
    {
        foreach (var language in LocalizationService.SupportedLanguages)
        {
            var keys = Directory.GetFiles(FindLanguageResourceDirectory(), language.Code + ".*.json")
                .SelectMany(file => Load(file).Keys)
                .ToList();

            keys.Should().OnlyHaveUniqueItems(language.Code);
        }
    }

    [Fact]
    public void LanguageFiles_UseProductReadyCopy()
    {
        foreach (var language in LocalizationService.SupportedLanguages)
        {
            var text = LocalizationService.LoadDictionary(language.Code);

            text["App.Name"].Should().Be("Kam");
            text["Settings.RuntimeVersionValue"].Should().Contain("v1.0.0");
            text.Values.Should().NotContain(value =>
                value.Contains("KAM NEURAL CORE", StringComparison.OrdinalIgnoreCase)
                || value.Contains("KERNEL", StringComparison.OrdinalIgnoreCase)
                || value.Contains("NEURAL", StringComparison.OrdinalIgnoreCase)
                || value.Contains("NÖRAL", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ÇEKİRDEK", StringComparison.OrdinalIgnoreCase)
                || value.Contains("KOORDİNATÖR", StringComparison.OrdinalIgnoreCase),
                language.Code);
        }
    }

    [Fact]
    public void TurkishCopy_UsesTurkishLettersAndTerms()
    {
        var turkish = LocalizationService.LoadDictionary("tr-TR");

        turkish["Settings.Title"].Should().Be("Ayarlar");
        turkish["Settings.RuntimeVersion"].Should().Be("Çalışma zamanı sürümü");
        turkish["Skills.Title"].Should().Be("Yetenekler");
        turkish.Values.Should().NotContain(value =>
            value.Contains("Calisma", StringComparison.Ordinal)
            || value.Contains("Surum", StringComparison.Ordinal)
            || value.Contains("Ayarlari", StringComparison.Ordinal));
    }

    [Fact]
    public void Views_UseOnlyKnownLanguageKeys()
    {
        var english = LocalizedXaml.English;
        var root = Path.GetFullPath(Path.Combine(FindLanguageResourceDirectory(), "..", ".."));
        var missing = new List<string>();

        foreach (var file in Directory.GetFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            missing.AddRange(MarkupKey().Matches(File.ReadAllText(file))
                .Select(match => match.Groups["key"].Value)
                .Where(key => !english.ContainsKey(key))
                .Select(key => $"{Path.GetFileName(file)}: {key}"));
        }

        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            missing.AddRange(CodeKey().Matches(File.ReadAllText(file))
                .Select(match => match.Groups["key"].Value)
                .Where(key => !english.ContainsKey(key))
                .Select(key => $"{Path.GetFileName(file)}: {key}"));
        }

        missing.Should().BeEmpty();
    }

    private static IReadOnlyDictionary<string, string> Load(string path)
    {
        using var stream = File.OpenRead(path);
        return LocalizationService.Parse(stream);
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(match => match.Value).Distinct().Order();

    internal static string FindLanguageResourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Ui", "SmartVoiceAgent.Ui", "Assets", "Lang");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate language resources from the test output directory.");
    }

    [GeneratedRegex(@"\{\d+(:[^}]*)?\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"DynamicResource\s+Lang\.(?<key>[A-Za-z0-9_.]+)")]
    private static partial Regex MarkupKey();

    [GeneratedRegex(@"Loc\.(?:Get|Format)\(\s*""(?<key>[^""]+)""")]
    private static partial Regex CodeKey();
}
