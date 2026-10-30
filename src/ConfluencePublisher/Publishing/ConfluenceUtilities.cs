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
    internal static string StableId(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatPublishAction(PublishAction action)
    {
        return action switch
        {
            PublishAction.Created => "Created",
            PublishAction.Updated => "Updated",
            _ => "Published"
        };
    }

    internal static ConfluencePage CreateDryRunPage(string title, string parentId, PublishAction action)
    {
        string id = StableId($"{parentId}:{title}")[..12];
        return new ConfluencePage
        {
            Id = id,
            Title = title,
            ParentId = parentId,
            Version = new ConfluenceVersion { Number = 1 },
            Links = new ConfluenceLinks { WebUi = $"/dry-run/{Uri.EscapeDataString(title)}" },
            PublishAction = action
        };
    }

    private static string AbsoluteWebUrl(string baseUrl, ConfluencePage page)
    {
        string? webUi = page.Links?.WebUi;
        if (string.IsNullOrWhiteSpace(webUi))
        {
            return $"{baseUrl}/pages/{page.Id}";
        }

        if (webUi.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            webUi.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return webUi;
        }

        if (webUi.StartsWith("/wiki/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{new Uri(baseUrl).GetLeftPart(UriPartial.Authority)}{webUi}";
        }

        return $"{baseUrl}{(webUi.StartsWith('/') ? string.Empty : "/")}{webUi}";
    }

    public static string EscapeXmlText(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    public static string EscapeXmlAttribute(string value)
    {
        return EscapeXmlText(value)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    public static string EscapeCData(string value)
    {
        return value.Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal);
    }

    public static bool IsRemoteUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSlash(string value)
    {
        return value.TrimEnd('/');
    }

    private static string GetEnv(string name, string defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string RequiredEnv(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value) || value.Contains("$(", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name} is required.");
        }

        return value;
    }

    private static bool IsTrue(string value)
    {
        return Regex.IsMatch(value, "^(1|true|yes)$", RegexOptions.IgnoreCase);
    }

    internal static string QueryString(IReadOnlyDictionary<string, string> values)
    {
        return string.Join("&", values.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
    }

    internal static string GetMimeType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
