using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Agent.Extensions;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Extensions;

public class MarkdownFrontmatterTests
{
    [Fact]
    public void Parse_ReadsFieldsAndBody()
    {
        const string text = "---\r\nname: pdf\r\ndescription: \"Extract text: tables, forms\"\r\nallowed-tools: [Read, Bash]\r\n---\r\n# PDF\r\n\r\nUse pdfplumber.\r\n";

        var (fields, body) = MarkdownFrontmatter.Parse(text);

        fields["name"].Should().Be("pdf");
        fields["description"].Should().Be("Extract text: tables, forms");
        fields["allowed-tools"].Should().Be("[Read, Bash]");
        body.Should().Be("# PDF\n\nUse pdfplumber.");
    }

    [Fact]
    public void Parse_FoldedAndLiteralBlocks()
    {
        const string text = """
            ---
            description: >
              Works with spreadsheets
              and CSV files.
            notes: |
              line one
              line two
            name: sheets
            ---
            Body
            """;

        var (fields, _) = MarkdownFrontmatter.Parse(text);

        fields["description"].Should().Be("Works with spreadsheets and CSV files.");
        fields["notes"].Should().Be("line one\nline two");
        fields["name"].Should().Be("sheets");
    }

    [Theory]
    [InlineData("# Just markdown\n\nNo frontmatter.")]
    [InlineData("---\nname: unterminated\nBody")]
    public void Parse_WithoutFrontmatter_ReturnsWholeTextAsBody(string text)
    {
        var (fields, body) = MarkdownFrontmatter.Parse(text);

        fields.Should().BeEmpty();
        body.Should().Be(text.Trim());
    }
}
