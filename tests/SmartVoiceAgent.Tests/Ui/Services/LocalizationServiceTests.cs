using FluentAssertions;
using SmartVoiceAgent.Ui.Services;
using System.Globalization;
using System.Text;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class LocalizationServiceTests
{
    [Theory]
    [InlineData("tr-TR", "tr-TR")]
    [InlineData("tr", "tr-TR")]
    [InlineData("TR_tr", "tr-TR")]
    [InlineData("en-GB", "en-US")]
    [InlineData("de-DE", "en-US")]
    [InlineData("", "en-US")]
    [InlineData(null, "en-US")]
    public void Normalize_MapsToSupportedLanguage(string? code, string expected)
    {
        LocalizationService.Normalize(code).Should().Be(expected);
    }

    [Fact]
    public void ResolveDefault_FollowsTurkishSystems()
    {
        LocalizationService.ResolveDefault(CultureInfo.GetCultureInfo("tr-TR")).Should().Be("tr-TR");
        LocalizationService.ResolveDefault(CultureInfo.GetCultureInfo("fr-FR")).Should().Be("en-US");
        LocalizationService.ResolveDefault(CultureInfo.InvariantCulture).Should().Be("en-US");
    }

    [Fact]
    public void SetLanguage_SwitchesTextAndRaisesEvent()
    {
        var service = new LocalizationService();
        var raised = 0;
        service.LanguageChanged += (_, _) => raised++;

        service.Get("Settings.Title").Should().Be("Settings");
        service.SetLanguage("tr");

        service.CurrentLanguage.Should().Be("tr-TR");
        service.IsTurkish.Should().BeTrue();
        service.Get("Settings.Title").Should().Be("Ayarlar");
        raised.Should().Be(1);
    }

    [Fact]
    public void Get_FallsBackToEnglishThenKey()
    {
        var service = new LocalizationService();
        service.SetLanguage("tr-TR");

        service.Get("No.Such.Key").Should().Be("No.Such.Key");
    }

    [Fact]
    public void Format_UsesLanguageCulture()
    {
        var service = new LocalizationService();
        service.SetLanguage("tr-TR");

        service.Culture.Name.Should().Be("tr-TR");
        string.Format(service.Culture, "{0:N1}", 1.5).Should().Be("1,5");
    }

    [Fact]
    public void Parse_SkipsCommentsAndNonStringValues()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
            {
              // comment
              "A": "one",
              "B": 2,
              "C": "üç",
            }
            """));

        LocalizationService.Parse(stream).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["A"] = "one",
            ["C"] = "üç"
        });
    }

    [Fact]
    public void Instances_KeepTheirOwnLanguage()
    {
        var turkish = new LocalizationService();
        turkish.SetLanguage("tr-TR");

        new LocalizationService().Get("Settings.Title").Should().Be("Settings");
        LocalizationService.Instance.CurrentLanguage.Should().Be("en-US");
    }
}
