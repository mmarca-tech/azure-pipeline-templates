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
    private static List<string> CollectMarkdownFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<DocsGroup> GroupByDocsSubfolder(string root, IEnumerable<string> files)
    {
        return files
            .GroupBy(file =>
            {
                string relative = Path.GetRelativePath(root, file);
                string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return parts.Length > 1 ? parts[0] : "docs";
            }, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DocsGroup(
                group.Key,
                group.OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    private static async Task WriteStorageSnapshotAsync(string workDir, string groupName, string title, string storage)
    {
        string outputDir = Path.Combine(workDir, "storage", SafeFileName(groupName));
        Directory.CreateDirectory(outputDir);
        string outputFile = Path.Combine(outputDir, $"{SafeFileName(title)}.storage.html");
        await File.WriteAllTextAsync(outputFile, storage);
    }

    private static string GetMarkdownTitle(string source, string filePath)
    {
        Match match = Regex.Match(source, @"^#\s+(.+)$", RegexOptions.Multiline);
        return match.Success
            ? StripInlineMarkdown(match.Groups[1].Value).Trim()
            : ToTitleCase(Path.GetFileNameWithoutExtension(filePath));
    }

    private static string StripInlineMarkdown(string value)
    {
        return Regex.Replace(
            Regex.Replace(
                Regex.Replace(value, @"`([^`]+)`", "$1"),
                @"\[([^\]]+)\]\([^)]+\)",
                "$1"),
            @"[*_~#]",
            string.Empty);
    }

    private static string ToTitleCase(string value)
    {
        string normalized = Regex.Replace(value.Replace('-', ' ').Replace('_', ' '), @"\s+", " ").Trim();
        return Regex.Replace(normalized, @"\b\w", match => match.Value.ToUpperInvariant());
    }

    public static string SafeFileName(string value)
    {
        string safe = Regex.Replace(value, @"[^a-zA-Z0-9._-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(safe)
            ? "page"
            : safe[..Math.Min(safe.Length, 120)];
    }
}
