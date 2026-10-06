using FluentAssertions;
using SmartVoiceAgent.Ui.Controls;

namespace SmartVoiceAgent.Tests.Ui.Controls;

public sealed class MarkdownViewTests
{
    [Fact]
    public void SplitBlocks_StreamedText_ChangesOnlyTheLastBlock()
    {
        var before = MarkdownView.SplitBlocks("## Plan\n\n- one\n- two\n\nWorking on");
        var after = MarkdownView.SplitBlocks("## Plan\n\n- one\n- two\n\nWorking on it now.");

        before.Should().HaveCount(3);
        after.Should().HaveCount(3);
        after.Take(2).Should().Equal(before.Take(2));
        after[2].Should().NotBe(before[2]);
    }

    [Fact]
    public void SplitBlocks_KeepsCodeAndTablesAsSingleBlocks()
    {
        var blocks = MarkdownView.SplitBlocks("```cs\nvar x = 1;\n\nvar y = 2;\n```\n\n| A | B |\n|---|---|\n| 1 | 2 |");

        blocks.Should().HaveCount(2);
        blocks[0].Should().StartWith("```cs").And.Contain("var y = 2;");
        blocks[1].Should().StartWith("| A | B |");
        MarkdownView.SplitBlocks(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://example.com/docs", true)]
    [InlineData("http://example.com", true)]
    [InlineData("mailto:team@example.com", true)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("docs/readme.md", false)]
    [InlineData(null, false)]
    public void TryGetSafeLink_OpensOnlyWebAndMailLinks(string? url, bool expected)
    {
        MarkdownView.TryGetSafeLink(url, out var uri).Should().Be(expected);
        (uri is not null).Should().Be(expected);
    }
}
