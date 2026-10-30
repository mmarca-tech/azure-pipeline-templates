using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SkiaSharp;
using Svg.Skia;

namespace PipelineTools.ConfluencePublisher;

public static partial class ConfluenceDocsPublisher
{
    private static string MarkdownToStorage(string source, string markdownFile, IReadOnlyDictionary<string, PreparedImage> imageMap)
    {
        MarkdownDocument document = Markdown.Parse(source, MarkdownPipeline);
        using StringWriter writer = new();
        HtmlRenderer renderer = new(writer);

        ReplaceRenderer<LinkInlineRenderer>(renderer, new ConfluenceLinkInlineRenderer(markdownFile, imageMap));
        ReplaceRenderer<CodeBlockRenderer>(renderer, new ConfluenceCodeBlockRenderer());

        MarkdownPipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return writer.ToString();
    }

    private static void ReplaceRenderer<TRenderer>(HtmlRenderer renderer, IMarkdownObjectRenderer replacement)
        where TRenderer : IMarkdownObjectRenderer
    {
        IMarkdownObjectRenderer? existing = renderer.ObjectRenderers.FirstOrDefault(item => item is TRenderer);
        if (existing is not null)
        {
            renderer.ObjectRenderers.Remove(existing);
        }

        renderer.ObjectRenderers.Add(replacement);
    }

    private static List<ImageReference> CollectImageReferences(string source, string markdownFile)
    {
        Regex regex = new(@"!\[(?<alt>[^\]]*)\]\((?<src><[^>]+>|[^\s)]+)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled);
        Dictionary<string, ImageReference> refs = new(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in regex.Matches(source))
        {
            string src = match.Groups["src"].Value.Trim();
            if (src.StartsWith('<') && src.EndsWith('>'))
            {
                src = src[1..^1];
            }

            ImageReference imageRef = ResolveImageReference(src, markdownFile, match.Groups["alt"].Value);
            refs[imageRef.Key] = imageRef;
        }

        return refs.Values.ToList();
    }

    internal static ImageReference ResolveImageReference(string src, string markdownFile, string alt)
    {
        if (string.IsNullOrWhiteSpace(src) || IsRemoteUrl(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return new ImageReference(src, src, alt, null);
        }

        string cleanSrc = src.Split(['?', '#'], 2)[0];
        string decodedSrc = Uri.UnescapeDataString(cleanSrc);
        string localPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(markdownFile) ?? string.Empty, decodedSrc));

        return new ImageReference(localPath, src, alt, localPath);
    }
}
