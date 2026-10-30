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
    private static string CreateIndexStorage(string title, IReadOnlyCollection<ChildPageLink> children)
    {
        string links = children.Count == 0
            ? string.Empty
            : $"<ul>{string.Concat(children.Select(child => $"<li><a href=\"{EscapeXmlAttribute(child.Url)}\">{EscapeXmlText(child.Title)}</a></li>"))}</ul>";

        return string.Join('\n', [
            $"<h1>{EscapeXmlText(title)}</h1>",
            "<p>Published from repository Markdown files.</p>",
            links
        ]);
    }

    private static string CreatePlaceholderStorage(string title)
    {
        return string.Join('\n', [
            $"<h1>{EscapeXmlText(title)}</h1>",
            "<p>Preparing attachments before publishing the final page.</p>"
        ]);
    }
}
