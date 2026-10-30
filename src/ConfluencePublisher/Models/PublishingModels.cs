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

enum PublishAction
{
    Unknown,
    Created,
    Updated
}

sealed record DocsGroup(string Name, List<string> Files);
sealed record ChildPageLink(string Title, string Url);
sealed record ImageReference(string Key, string Source, string Alt, string? LocalPath);
sealed record PreparedImage(string FileName, string Alt, int? Width);
sealed record PreparedImageFile(string FilePath, string FileName);
