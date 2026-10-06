using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MarkdownTable = Markdig.Extensions.Tables.Table;

namespace SmartVoiceAgent.Ui.Controls;

/// <summary>
/// Renders Markdown as selectable text: headings, paragraphs, lists, quotes, tables and code blocks
/// with a copy button. Blocks whose source did not change keep their controls, so a streaming reply
/// only rebuilds its last block.
/// </summary>
public sealed class MarkdownView : Decorator
{
    /// <summary>The Markdown to render.</summary>
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .Build();

    private static Cursor? s_handCursor;

    private readonly StackPanel _panel = new() { Spacing = 10 };
    private readonly List<RenderedBlock> _rendered = [];
    private bool _isDirty;

    static MarkdownView()
    {
        // Rendering waits for layout: a hidden view (such as the agent part of a user message row)
        // never parses, and several streamed updates between two frames parse once.
        MarkdownProperty.Changed.AddClassHandler<MarkdownView>((view, _) =>
        {
            view._isDirty = true;
            view.InvalidateMeasure();
        });
    }

    /// <summary>Creates the view.</summary>
    public MarkdownView()
    {
        Child = _panel;
    }

    /// <summary>Gets or sets the Markdown to render.</summary>
    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>Gets the number of top-level blocks on screen, for tests.</summary>
    public int BlockCount => _rendered.Count;

    /// <summary>Gets how many blocks the last render built rather than reused, for tests.</summary>
    public int LastBuiltBlockCount { get; private set; }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_isDirty)
        {
            _isDirty = false;
            Render();
        }

        return base.MeasureOverride(availableSize);
    }

    /// <summary>
    /// Splits Markdown into the source text of its top-level blocks. A block whose text is unchanged
    /// since the last render keeps its controls, so a streamed reply rebuilds only its last block.
    /// </summary>
    public static IReadOnlyList<string> SplitBlocks(string? markdown)
    {
        var text = markdown ?? string.Empty;
        return ParseBlocks(text).Select(block => SourceOf(text, block)).ToList();
    }

    /// <summary>
    /// Accepts only web and mail links, so a reply cannot open files or other apps.
    /// </summary>
    public static bool TryGetSafeLink(string? url, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "http" or "https" or "mailto")
        {
            uri = parsed;
        }

        return uri is not null;
    }

    private static List<Block> ParseBlocks(string markdown)
    {
        return Markdig.Markdown.Parse(markdown, Pipeline)
            .Where(block => block is not LinkReferenceDefinitionGroup)
            .ToList();
    }

    private void Render()
    {
        var markdown = Markdown ?? string.Empty;
        var blocks = ParseBlocks(markdown);

        var built = 0;
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            var source = SourceOf(markdown, block);
            if (index < _rendered.Count && _rendered[index].Source == source)
            {
                continue;
            }

            var control = BuildBlock(block, markdown);
            built++;
            if (index < _rendered.Count)
            {
                _rendered[index] = new RenderedBlock(source, control);
                _panel.Children[index] = control;
            }
            else
            {
                _rendered.Add(new RenderedBlock(source, control));
                _panel.Children.Add(control);
            }
        }

        while (_rendered.Count > blocks.Count)
        {
            _rendered.RemoveAt(_rendered.Count - 1);
            _panel.Children.RemoveAt(_panel.Children.Count - 1);
        }

        LastBuiltBlockCount = built;
    }

    private static string SourceOf(string markdown, Block block)
    {
        var start = Math.Clamp(block.Span.Start, 0, markdown.Length);
        var length = Math.Clamp(block.Span.Length, 0, markdown.Length - start);
        return markdown.Substring(start, length);
    }

    private Control BuildBlock(Block block, string markdown)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var headingText = CreateText(heading.Inline);
                headingText.Classes.Add("MarkdownHeading");
                headingText.Classes.Add(heading.Level switch { 1 => "H1", 2 => "H2", _ => "H3" });
                return headingText;

            case ParagraphBlock paragraph:
                return CreateText(paragraph.Inline);

            case CodeBlock code:
                return CreateCodeBlock(code);

            case ListBlock list:
                return CreateList(list, markdown);

            case QuoteBlock quote:
                var quoteBody = new StackPanel { Spacing = 8 };
                foreach (var child in quote)
                {
                    quoteBody.Children.Add(BuildBlock(child, markdown));
                }

                var quoteBorder = new Border { Child = quoteBody };
                quoteBorder.Classes.Add("MarkdownQuote");
                return quoteBorder;

            case ThematicBreakBlock:
                var rule = new Border();
                rule.Classes.Add("MarkdownRule");
                return rule;

            case MarkdownTable table:
                return CreateTable(table);

            case HtmlBlock html:
                return CreatePlainText(string.Join('\n', html.Lines.Lines.Take(html.Lines.Count).Select(line => line.Slice.ToString())));

            case ContainerBlock container:
                var panel = new StackPanel { Spacing = 8 };
                foreach (var child in container)
                {
                    panel.Children.Add(BuildBlock(child, markdown));
                }

                return panel;

            default:
                return CreatePlainText(SourceOf(markdown, block));
        }
    }

    private Control CreateList(ListBlock list, string markdown)
    {
        var panel = new StackPanel { Spacing = list.IsLoose ? 8 : 4 };
        panel.Classes.Add("MarkdownList");

        var number = 1;
        if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start))
        {
            number = start;
        }

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var content = new StackPanel { Spacing = 6 };
            var taskState = (item.FirstOrDefault() as ParagraphBlock)?.Inline?.FirstChild as TaskList;
            foreach (var child in item)
            {
                content.Children.Add(BuildBlock(child, markdown));
            }

            var marker = new TextBlock
            {
                Text = taskState is not null
                    ? taskState.Checked ? "☑" : "☐"
                    : list.IsOrdered ? $"{number}{list.OrderedDelimiter}" : "•",
                MinWidth = list.IsOrdered ? 22 : 14
            };
            marker.Classes.Add("MarkdownListMarker");

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumn(content, 1);
            row.Children.Add(marker);
            row.Children.Add(content);
            panel.Children.Add(row);
            number++;
        }

        return panel;
    }

    private Control CreateCodeBlock(CodeBlock code)
    {
        var text = CodeText(code);
        var language = code is FencedCodeBlock fenced ? fenced.Info?.Trim() ?? string.Empty : string.Empty;

        var body = new SelectableTextBlock { Text = text };
        body.Classes.Add("MarkdownCode");

        var scroller = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };

        var label = new TextBlock { Text = string.IsNullOrWhiteSpace(language) ? "code" : language };
        label.Classes.Add("MarkdownCodeLanguage");

        var copy = CreateCopyButton(() => text, "Copy code");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(copy, 1);
        header.Children.Add(label);
        header.Children.Add(copy);
        header.Classes.Add("MarkdownCodeHeader");

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(scroller);

        var border = new Border { Child = layout };
        border.Classes.Add("MarkdownCodeBlock");
        return border;
    }

    private Control CreateTable(MarkdownTable table)
    {
        var columnCount = Math.Max(1, table.ColumnDefinitions.Count);
        var rows = table.OfType<TableRow>().ToList();
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("Auto", columnCount))),
            RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", Math.Max(1, rows.Count))))
        };

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var cells = row.OfType<TableCell>().ToList();
            for (var cellIndex = 0; cellIndex < cells.Count && cellIndex < columnCount; cellIndex++)
            {
                var cell = cells[cellIndex];
                var inline = (cell.FirstOrDefault() as ParagraphBlock)?.Inline;
                var text = CreateText(inline);
                text.TextWrapping = TextWrapping.Wrap;
                text.MaxWidth = 360;
                var alignment = cellIndex < table.ColumnDefinitions.Count ? table.ColumnDefinitions[cellIndex].Alignment : null;
                text.TextAlignment = alignment switch
                {
                    TableColumnAlign.Center => TextAlignment.Center,
                    TableColumnAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left
                };

                var cellBorder = new Border { Child = text };
                cellBorder.Classes.Add("MarkdownTableCell");
                if (row.IsHeader)
                {
                    cellBorder.Classes.Add("Header");
                }

                Grid.SetRow(cellBorder, rowIndex);
                Grid.SetColumn(cellBorder, cellIndex);
                grid.Children.Add(cellBorder);
            }
        }

        var frame = new Border { Child = grid, HorizontalAlignment = HorizontalAlignment.Left };
        frame.Classes.Add("MarkdownTable");
        return new ScrollViewer
        {
            Content = frame,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
    }

    private SelectableTextBlock CreatePlainText(string text)
    {
        var block = new SelectableTextBlock { Text = text };
        block.Classes.Add("Markdown");
        return block;
    }

    private SelectableTextBlock CreateText(ContainerInline? inline)
    {
        var block = new SelectableTextBlock();
        block.Classes.Add("Markdown");
        var links = new List<LinkRange>();
        var inlines = new InlineCollection();
        var offset = 0;
        if (inline is not null)
        {
            AppendInlines(inline, inlines, InlineStyle.None, links, ref offset);
        }

        block.Inlines = inlines;
        if (links.Count > 0)
        {
            AttachLinks(block, links);
        }

        return block;
    }

    private void AppendInlines(
        ContainerInline container,
        InlineCollection target,
        InlineStyle style,
        List<LinkRange> links,
        ref int offset)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case TaskList:
                    // Drawn as the list marker.
                    break;

                case LiteralInline literal:
                    AddRun(target, literal.Content.ToString(), style, ref offset);
                    break;

                case CodeInline code:
                    AddRun(target, code.Content, style | InlineStyle.Code, ref offset);
                    break;

                case LineBreakInline lineBreak:
                    AddRun(target, lineBreak.IsHard ? "\n" : " ", style, ref offset);
                    break;

                case HtmlEntityInline entity:
                    AddRun(target, entity.Transcoded.ToString(), style, ref offset);
                    break;

                case HtmlInline html:
                    AddRun(target, html.Tag, style, ref offset);
                    break;

                case AutolinkInline autolink:
                    AddLink(target, autolink.Url, autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, style, links, ref offset);
                    break;

                case LinkInline link when link.IsImage:
                    AddRun(target, string.IsNullOrWhiteSpace(link.Title) ? "[image]" : $"[image: {link.Title}]", style, ref offset);
                    break;

                case LinkInline link:
                    var start = offset;
                    AppendInlines(link, target, style | InlineStyle.Link, links, ref offset);
                    if (offset == start)
                    {
                        AddRun(target, link.Url ?? string.Empty, style | InlineStyle.Link, ref offset);
                    }

                    if (!string.IsNullOrWhiteSpace(link.Url))
                    {
                        links.Add(new LinkRange(start, offset - start, link.Url));
                    }

                    break;

                case EmphasisInline emphasis:
                    var emphasisStyle = emphasis.DelimiterChar == '~'
                        ? InlineStyle.Strike
                        : emphasis.DelimiterCount >= 2 ? InlineStyle.Bold : InlineStyle.Italic;
                    AppendInlines(emphasis, target, style | emphasisStyle, links, ref offset);
                    break;

                case ContainerInline nested:
                    AppendInlines(nested, target, style, links, ref offset);
                    break;

                default:
                    AddRun(target, inline.ToString() ?? string.Empty, style, ref offset);
                    break;
            }
        }
    }

    private void AddLink(
        InlineCollection target,
        string text,
        string url,
        InlineStyle style,
        List<LinkRange> links,
        ref int offset)
    {
        var start = offset;
        AddRun(target, text, style | InlineStyle.Link, ref offset);
        links.Add(new LinkRange(start, offset - start, url));
    }

    private void AddRun(InlineCollection target, string text, InlineStyle style, ref int offset)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var run = new Run(text);
        if (style.HasFlag(InlineStyle.Bold))
        {
            run.FontWeight = FontWeight.SemiBold;
        }

        if (style.HasFlag(InlineStyle.Italic))
        {
            run.FontStyle = FontStyle.Italic;
        }

        if (style.HasFlag(InlineStyle.Strike))
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }

        if (style.HasFlag(InlineStyle.Code))
        {
            run.Bind(TextElement.FontFamilyProperty, this.GetResourceObservable("MonoFontFamily"));
            run.Bind(TextElement.BackgroundProperty, this.GetResourceObservable("CardBgHoverBrush"));
            run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("TextPrimaryBrush"));
            run.FontSize = 13;
        }

        if (style.HasFlag(InlineStyle.Link))
        {
            run.TextDecorations = TextDecorations.Underline;
            run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("AccentBrush"));
        }
        else if (style.HasFlag(InlineStyle.Bold) && !style.HasFlag(InlineStyle.Code))
        {
            run.Bind(TextElement.ForegroundProperty, this.GetResourceObservable("TextPrimaryBrush"));
        }

        target.Add(run);
        offset += text.Length;
    }

    private static void AttachLinks(SelectableTextBlock block, IReadOnlyList<LinkRange> links)
    {
        block.PointerMoved += (_, e) =>
        {
            block.Cursor = FindLink(block, links, e.GetPosition(block)) is null
                ? Cursor.Default
                : s_handCursor ??= new Cursor(StandardCursorType.Hand);
        };

        block.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left || block.SelectionStart != block.SelectionEnd)
            {
                return;
            }

            if (TryGetSafeLink(FindLink(block, links, e.GetPosition(block)), out var uri))
            {
                _ = TopLevel.GetTopLevel(block)?.Launcher.LaunchUriAsync(uri);
            }
        };
    }

    private static string? FindLink(TextBlock block, IReadOnlyList<LinkRange> links, Point point)
    {
        var hit = block.TextLayout.HitTestPoint(point - new Point(block.Padding.Left, block.Padding.Top));
        if (!hit.IsInside)
        {
            return null;
        }

        var position = hit.TextPosition;
        return links.FirstOrDefault(link => position >= link.Start && position < link.Start + link.Length)?.Url;
    }

    /// <summary>
    /// Creates the small copy button used on code blocks. It shows "Copied" for a moment after a click.
    /// </summary>
    /// <param name="text">Returns the text to copy.</param>
    /// <param name="tip">The button's tooltip.</param>
    public static Button CreateCopyButton(Func<string> text, string tip)
    {
        var label = new TextBlock { Text = "Copy" };
        var icon = new Avalonia.Controls.Shapes.Path { Classes = { "Icon" } };
        icon.Bind(Avalonia.Controls.Shapes.Path.DataProperty, icon.GetResourceObservable("IconCopy"));
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children = { new Viewbox { Width = 12, Height = 12, Child = icon }, label }
        };

        var button = new Button { Content = content };
        button.Classes.Add("MarkdownCopy");
        ToolTip.SetTip(button, tip);
        button.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(button)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            await clipboard.SetTextAsync(text());
            label.Text = "Copied";
            await Task.Delay(1500);
            label.Text = "Copy";
        };

        return button;
    }

    private static string CodeText(LeafBlock code)
    {
        var builder = new StringBuilder();
        var lines = code.Lines;
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            builder.Append(lines.Lines[index].Slice.ToString());
        }

        return builder.ToString().TrimEnd('\n');
    }

    [Flags]
    private enum InlineStyle
    {
        None = 0,
        Bold = 1,
        Italic = 2,
        Code = 4,
        Link = 8,
        Strike = 16
    }

    private sealed record LinkRange(int Start, int Length, string Url);

    private sealed record RenderedBlock(string Source, Control Control);
}
