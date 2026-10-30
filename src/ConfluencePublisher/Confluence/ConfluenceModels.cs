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

sealed class ConfluenceListResponse<T>
{
    [JsonPropertyName("results")]
    public List<T> Results { get; set; } = [];
}

sealed class ConfluenceContentSearchResponse
{
    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("results")]
    public List<ConfluenceContent> Results { get; set; } = [];
}

sealed class ConfluenceSpace
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;
}

sealed class ConfluencePage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    [JsonPropertyName("version")]
    public ConfluenceVersion? Version { get; set; }

    [JsonPropertyName("_links")]
    public ConfluenceLinks? Links { get; set; }

    [JsonIgnore]
    public PublishAction PublishAction { get; set; }
}

sealed class ConfluenceContent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public ConfluenceVersion? Version { get; set; }

    [JsonPropertyName("_links")]
    public ConfluenceLinks? Links { get; set; }

    [JsonPropertyName("ancestors")]
    public List<ConfluenceAncestor> Ancestors { get; set; } = [];
}

sealed class ConfluenceAncestor
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

sealed class ConfluenceVersion
{
    [JsonPropertyName("number")]
    public int Number { get; set; }
}

sealed class ConfluenceLinks
{
    [JsonPropertyName("webui")]
    public string? WebUi { get; set; }
}

sealed class ConfluenceAttachment
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

sealed class ConfluenceRequestException : HttpRequestException
{
    public ConfluenceRequestException(string message, int statusCode, string responseText)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseText = responseText;
    }

    public new int StatusCode { get; }

    public string ResponseText { get; }
}
