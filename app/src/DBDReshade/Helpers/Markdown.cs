using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DBDReshade.Helpers;

/// <summary>
/// Shows GitHub-flavoured Markdown (release notes) in a StackPanel: <c>h:Markdown.Text="{Binding Body}"</c>.
/// Covers what release notes use: headings, bullet and numbered lists, **bold**, *italic*, `code`, links,
/// quotes, code blocks and separators. HTML tags and images are skipped.
/// </summary>
public static partial class Markdown
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Markdown), new PropertyMetadata(null, (d, e) =>
        {
            if (d is Panel panel) Render(panel, e.NewValue as string ?? "");
        }));

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);
    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    [GeneratedRegex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*\s*$")]
    private static partial Regex Heading();

    // **bold**, __bold__, `code`, ![image](url), [text](url), bare URL, *italic*, _italic_
    [GeneratedRegex(@"(\*\*.+?\*\*|__.+?__|`[^`]+`|!\[[^\]]*\]\([^)]*\)|\[[^\]]+\]\([^)\s]+\)|https?://[^\s)]+|\*[^*\s][^*]*?\*|(?<![\w])_[^_\s][^_]*?_(?![\w]))")]
    private static partial Regex InlineToken();

    private static void Render(Panel panel, string markdown)
    {
        panel.Children.Clear();
        var paragraph = new StringBuilder();
        var code = (StringBuilder?)null;

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            panel.Children.Add(Text(paragraph.ToString(), new Thickness(0, 0, 0, 8)));
            paragraph.Clear();
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();

            // ``` code blocks
            if (line.TrimStart().StartsWith("```"))
            {
                if (code is null) { FlushParagraph(); code = new StringBuilder(); }
                else { panel.Children.Add(CodeBlock(code.ToString().TrimEnd('\n'))); code = null; }
                continue;
            }
            if (code is not null) { code.Append(raw).Append('\n'); continue; }

            if (string.IsNullOrWhiteSpace(line)) { FlushParagraph(); continue; }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('<')) continue; // HTML (<details>, comments...): not shown

            if (Heading().Match(trimmed) is { Success: true } h)
            {
                FlushParagraph();
                var level = h.Groups[1].Value.Length;
                var block = Text(h.Groups[2].Value, new Thickness(0, panel.Children.Count == 0 ? 0 : 8, 0, 6));
                block.FontWeight = FontWeights.Bold;
                block.FontSize = level switch { 1 => 18, 2 => 16, _ => 14 };
                panel.Children.Add(block);
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                panel.Children.Add(new Rectangle
                {
                    Height = 1, Margin = new Thickness(0, 6, 0, 10),
                    Fill = (Brush)Application.Current.FindResource("BorderBrush"),
                });
                continue;
            }

            if (ListItem().Match(line) is { Success: true } li)
            {
                FlushParagraph();
                var indent = li.Groups[1].Value.Replace("\t", "    ").Length / 2;
                var marker = char.IsDigit(li.Groups[2].Value[0]) ? li.Groups[2].Value : "•";
                panel.Children.Add(Bullet(marker, li.Groups[3].Value, indent));
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                var quote = Text(trimmed.TrimStart('>', ' '), new Thickness(0));
                quote.Foreground = (Brush)Application.Current.FindResource("TextMutedBrush");
                panel.Children.Add(new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 2, 0, 2),
                    Margin = new Thickness(0, 0, 0, 8),
                    BorderBrush = (Brush)Application.Current.FindResource("BorderBrush"),
                    Child = quote,
                });
                continue;
            }

            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(trimmed);
        }

        FlushParagraph();
        if (code is not null) panel.Children.Add(CodeBlock(code.ToString().TrimEnd('\n')));
    }

    private static UIElement Bullet(string marker, string text, int indent)
    {
        var grid = new Grid { Margin = new Thickness(4 + indent * 18, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var dot = new TextBlock
        {
            Text = marker,
            Foreground = (Brush)Application.Current.FindResource("AccentBrush"),
            FontWeight = FontWeights.Bold,
        };
        var body = Text(text, new Thickness(0));
        Grid.SetColumn(body, 1);
        grid.Children.Add(dot);
        grid.Children.Add(body);
        return grid;
    }

    private static UIElement CodeBlock(string text) => new Border
    {
        Background = (Brush)Application.Current.FindResource("BackgroundBrush"),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 8, 10, 8),
        Margin = new Thickness(0, 0, 0, 8),
        Child = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.Wrap },
    };

    private static TextBlock Text(string text, Thickness margin)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = margin, LineHeight = 19 };
        AddInlines(block.Inlines, text);
        return block;
    }

    private static void AddInlines(InlineCollection inlines, string text)
    {
        var last = 0;
        foreach (Match m in InlineToken().Matches(text))
        {
            if (m.Index > last) inlines.Add(new Run(text[last..m.Index]));
            last = m.Index + m.Length;
            var token = m.Value;

            if (token.StartsWith("**") || token.StartsWith("__"))
            {
                var bold = new Bold();
                AddInlines(bold.Inlines, token[2..^2]);
                inlines.Add(bold);
            }
            else if (token.StartsWith('`'))
            {
                inlines.Add(new Run(token[1..^1])
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = (Brush)Application.Current.FindResource("BackgroundBrush"),
                });
            }
            else if (token.StartsWith("!["))
            {
                // images are not shown
            }
            else if (token.StartsWith('['))
            {
                var close = token.IndexOf("](", StringComparison.Ordinal);
                inlines.Add(Link(token[1..close], token[(close + 2)..^1]));
            }
            else if (token.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                inlines.Add(Link(token, token));
            }
            else
            {
                var italic = new Italic();
                AddInlines(italic.Inlines, token[1..^1]);
                inlines.Add(italic);
            }
        }
        if (last < text.Length) inlines.Add(new Run(text[last..]));
    }

    private static Hyperlink Link(string text, string url)
    {
        // Relative links in release notes point into the repository.
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = $"{AppInfo.RepoUrl}/blob/{AppInfo.DefaultBranch}/{url.TrimStart('.', '/')}";

        var link = new Hyperlink(new Run(text))
        {
            Foreground = (Brush)Application.Current.FindResource("LinkBrush"),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = url,
        };
        link.Click += (_, _) => Shell.Open(url);
        return link;
    }
}
