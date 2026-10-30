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

sealed class ConfluenceLinkInlineRenderer : LinkInlineRenderer
{
    private readonly string markdownFile;
    private readonly IReadOnlyDictionary<string, PreparedImage> imageMap;

    public ConfluenceLinkInlineRenderer(string markdownFile, IReadOnlyDictionary<string, PreparedImage> imageMap)
    {
        this.markdownFile = markdownFile;
        this.imageMap = imageMap;
    }

    protected override void Write(HtmlRenderer renderer, LinkInline link)
    {
        if (!link.IsImage)
        {
            base.Write(renderer, link);
            return;
        }

        string src = link.GetDynamicUrl is not null
            ? link.GetDynamicUrl() ?? link.Url ?? string.Empty
            : link.Url ?? string.Empty;

        string alt = ExtractPlainText(link);
        ImageReference imageRef = ConfluenceDocsPublisher.ResolveImageReference(src, markdownFile, alt);

        if (imageRef.LocalPath is not null)
        {
            if (!imageMap.TryGetValue(imageRef.Key, out PreparedImage? uploaded))
            {
                return;
            }

            string width = uploaded.Width is null
                ? string.Empty
                : $" ac:width=\"{ConfluenceDocsPublisher.EscapeXmlAttribute(uploaded.Width.Value.ToString())}\"";

            renderer.Write($"<ac:image ac:alt=\"{ConfluenceDocsPublisher.EscapeXmlAttribute(uploaded.Alt)}\"{width}><ri:attachment ri:filename=\"{ConfluenceDocsPublisher.EscapeXmlAttribute(uploaded.FileName)}\" /></ac:image>");
            return;
        }

        if (ConfluenceDocsPublisher.IsRemoteUrl(src))
        {
            renderer.Write($"<ac:image ac:alt=\"{ConfluenceDocsPublisher.EscapeXmlAttribute(alt)}\"><ri:url ri:value=\"{ConfluenceDocsPublisher.EscapeXmlAttribute(src)}\" /></ac:image>");
        }
    }

    private static string ExtractPlainText(ContainerInline inline)
    {
        StringBuilder builder = new();

        for (Inline? child = inline.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
                case ContainerInline container:
                    builder.Append(ExtractPlainText(container));
                    break;
            }
        }

        return builder.ToString();
    }
}
