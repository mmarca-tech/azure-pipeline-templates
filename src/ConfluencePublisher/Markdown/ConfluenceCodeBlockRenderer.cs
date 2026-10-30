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

sealed class ConfluenceCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
{
    protected override void Write(HtmlRenderer renderer, CodeBlock codeBlock)
    {
        string language = codeBlock is FencedCodeBlock fencedCodeBlock
            ? (fencedCodeBlock.Info ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty
            : string.Empty;

        string languageParameter = string.IsNullOrWhiteSpace(language)
            ? string.Empty
            : $"<ac:parameter ac:name=\"language\">{ConfluenceDocsPublisher.EscapeXmlText(language)}</ac:parameter>";

        renderer.Write("<ac:structured-macro ac:name=\"code\" ac:schema-version=\"1\">");
        renderer.Write(languageParameter);
        renderer.Write("<ac:plain-text-body><![CDATA[");
        renderer.Write(ConfluenceDocsPublisher.EscapeCData(codeBlock.Lines.ToString() ?? string.Empty));
        renderer.Write("]]></ac:plain-text-body>");
        renderer.Write("</ac:structured-macro>\n");
    }
}
