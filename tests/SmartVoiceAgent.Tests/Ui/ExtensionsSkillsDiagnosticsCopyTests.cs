using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Agent.Extensions;
using SmartVoiceAgent.Ui.Services;
using System.Text.RegularExpressions;

namespace SmartVoiceAgent.Tests.Ui;

/// <summary>
/// Pins that the Extensions, Skills and Diagnostics pages read their copy from the language files.
/// </summary>
public sealed partial class ExtensionsSkillsDiagnosticsCopyTests
{
    [Theory]
    [InlineData("ExtensionsView.axaml")]
    [InlineData("PluginsView.axaml")]
    [InlineData("RuntimeDiagnosticsView.axaml")]
    public void View_HasNoLiteralCopy(string viewFileName)
    {
        var markup = File.ReadAllText(FindView(viewFileName));

        LiteralCopy().Matches(markup).Select(match => match.Value).Should().BeEmpty();
    }

    [Fact]
    public void TurkishCopy_UsesGlossaryTerms()
    {
        var turkish = LocalizationService.LoadDictionary("tr-TR");

        turkish["Extensions.Title"].Should().Be("Uzantılar");
        turkish["Extensions.Mcp.Title"].Should().Be("MCP sunucuları");
        turkish["Extensions.Plugins.Title"].Should().Be("Eklentiler");
        turkish["Extensions.Skills.Title"].Should().Be("Yetenekler");
        turkish["Extensions.Commands.Title"].Should().Be("Slash komutları");
        turkish["Diagnostics.Title"].Should().Be("Çalışma zamanı tanılaması");
        turkish["Diagnostics.Status.ActionNeeded"].Should().Be("İŞLEM GEREKLİ");
        turkish["Skills.Card.ApproveReview"].Should().Be("İncelemeyi onayla");
    }

    [Fact]
    public void ExtensionCatalog_HasCopyForEveryEntryInBothLanguages()
    {
        var english = LocalizationService.LoadDictionary("en-US");
        var turkish = LocalizationService.LoadDictionary("tr-TR");
        var ids = ExtensionCatalog.McpServers.Select(entry => entry.Name).Concat(ExtensionCatalog.Plugins.Select(entry => entry.Id));

        foreach (var id in ids)
        {
            english.Should().ContainKey("Extensions.Catalog." + id);
            turkish.Should().ContainKey("Extensions.Catalog." + id);
        }
    }

    [Fact]
    public void TurkishCopy_FormatsCountsAndRatesForTurkish()
    {
        var service = new LocalizationService();
        service.SetLanguage("tr-TR");

        service.Format("Extensions.Count.McpServer.Other", 3).Should().Be("3 MCP sunucusu");
        service.Format("Skills.Metrics.Recent", 3, 4, 75d).Should().Be("Son çalıştırmalar: 3/4 başarılı (%75,0)");
        service.Format("Diagnostics.SkillSmoke.Passing", 2, 2).Should().Be("2/2 duman testi geçiyor");
    }

    private static string FindView(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Ui", "SmartVoiceAgent.Ui", "Views", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{fileName} was not found.");
    }

    [GeneratedRegex(@"\b(?:Text|Content|ToolTip\.Tip|PlaceholderText|Watermark|Header|OnContent|OffContent|AutomationProperties\.Name)=""[^{""][^""]*""")]
    private static partial Regex LiteralCopy();
}
