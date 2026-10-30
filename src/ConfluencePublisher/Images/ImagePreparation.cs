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
    private static async Task<PreparedImageFile> PrepareImageFileAsync(string localPath, string pageId, string workDir)
    {
        string extension = Path.GetExtension(localPath).ToLowerInvariant();
        string baseName = Path.GetFileNameWithoutExtension(localPath);
        string hash = StableId(localPath)[..8];
        string mediaDir = Path.Combine(workDir, "media", pageId);
        Directory.CreateDirectory(mediaDir);

        if (extension == ".svg")
        {
            string fileName = $"{SafeFileName(baseName)}-{hash}.png";
            string outputPath = Path.Combine(mediaDir, fileName);
            ConvertSvgToPng(localPath, outputPath);
            return new PreparedImageFile(outputPath, fileName);
        }

        string stableFileName = $"{SafeFileName(baseName)}-{hash}{extension}";
        string copiedPath = Path.Combine(mediaDir, stableFileName);
        await using FileStream source = File.OpenRead(localPath);
        await using FileStream destination = File.Create(copiedPath);
        await source.CopyToAsync(destination);

        return new PreparedImageFile(copiedPath, stableFileName);
    }

    private static void ConvertSvgToPng(string inputPath, string outputPath)
    {
        string rasterInputPath = PrepareSvgForRasterization(inputPath);

        using SKSvg svg = new();
        try
        {
            svg.Load(rasterInputPath);
        }
        finally
        {
            if (!string.Equals(rasterInputPath, inputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(rasterInputPath))
            {
                File.Delete(rasterInputPath);
            }
        }

        SKPicture? picture = svg.Picture;
        if (picture is null)
        {
            throw new InvalidOperationException($"Unable to load SVG: {inputPath}");
        }

        SKRect bounds = picture.CullRect;
        float scale = 2f;
        int width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream output = File.Create(outputPath);
        data.SaveTo(output);
    }

    private static string PrepareSvgForRasterization(string inputPath)
    {
        string svg = File.ReadAllText(inputPath);
        if (!svg.Contains("<foreignObject", StringComparison.OrdinalIgnoreCase))
        {
            return inputPath;
        }

        XDocument document = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);
        XNamespace svgNamespace = document.Root?.Name.Namespace ?? XNamespace.None;
        List<XElement> foreignObjects = document
            .Descendants()
            .Where(element => element.Name.LocalName == "foreignObject")
            .ToList();

        foreach (XElement foreignObject in foreignObjects)
        {
            foreignObject.ReplaceWith(CreateNativeSvgText(svgNamespace, foreignObject));
        }

        string tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{Path.GetFileNameWithoutExtension(inputPath)}-{Guid.NewGuid():N}.svg");

        document.Save(tempPath, SaveOptions.DisableFormatting);
        return tempPath;
    }

    private static XElement CreateNativeSvgText(XNamespace svgNamespace, XElement foreignObject)
    {
        List<string> lines = ExtractForeignObjectTextLines(foreignObject);
        if (lines.Count == 0)
        {
            return new XElement(svgNamespace + "g");
        }

        double x = ReadSvgDouble(foreignObject.Attribute("x")?.Value);
        double y = ReadSvgDouble(foreignObject.Attribute("y")?.Value);
        double width = ReadSvgDouble(foreignObject.Attribute("width")?.Value);
        double height = ReadSvgDouble(foreignObject.Attribute("height")?.Value);

        string style = string.Join(
            ';',
            foreignObject.Descendants().Select(element => element.Attribute("style")?.Value).Where(value => !string.IsNullOrWhiteSpace(value)));

        bool alignStart = style.Contains("text-align: start", StringComparison.OrdinalIgnoreCase) ||
                          style.Contains("text-align:left", StringComparison.OrdinalIgnoreCase) ||
                          style.Contains("text-align: left", StringComparison.OrdinalIgnoreCase);

        double fontSize = 16d;
        double lineHeight = height > 0
            ? Math.Clamp(height / lines.Count, 18d, 24d)
            : 20d;

        double totalTextHeight = lineHeight * lines.Count;
        double firstBaseline = y + Math.Max(fontSize, ((height - totalTextHeight) / 2d) + fontSize);
        double textX = alignStart ? x : x + (width / 2d);

        XElement text = new(svgNamespace + "text",
            new XAttribute("x", FormatSvgDouble(textX)),
            new XAttribute("y", FormatSvgDouble(firstBaseline)),
            new XAttribute("font-family", "Arial, Helvetica, sans-serif"),
            new XAttribute("font-size", FormatSvgDouble(fontSize)),
            new XAttribute("fill", "#2f3337"),
            new XAttribute("stroke", "none"),
            new XAttribute("stroke-width", "0"),
            new XAttribute("style", "fill:#2f3337;stroke:none;stroke-width:0;font-weight:400;"),
            new XAttribute("text-anchor", alignStart ? "start" : "middle"));

        for (int index = 0; index < lines.Count; index++)
        {
            XElement tspan = new(svgNamespace + "tspan",
                new XAttribute("x", FormatSvgDouble(textX)),
                new XAttribute("fill", "#2f3337"),
                new XAttribute("stroke", "none"),
                new XAttribute("stroke-width", "0"),
                new XAttribute("style", "fill:#2f3337;stroke:none;stroke-width:0;font-weight:400;"),
                new XText(lines[index]));

            if (index == 0)
            {
                tspan.SetAttributeValue("y", FormatSvgDouble(firstBaseline));
            }
            else
            {
                tspan.SetAttributeValue("dy", FormatSvgDouble(lineHeight));
            }

            text.Add(tspan);
        }

        return text;
    }

    private static List<string> ExtractForeignObjectTextLines(XElement foreignObject)
    {
        List<string> lines = [];
        StringBuilder currentLine = new();

        void AddLine()
        {
            string line = Regex.Replace(currentLine.ToString(), @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }

            currentLine.Clear();
        }

        void Walk(XNode node)
        {
            switch (node)
            {
                case XText text:
                    currentLine.Append(text.Value);
                    break;
                case XElement element when element.Name.LocalName.Equals("br", StringComparison.OrdinalIgnoreCase):
                    AddLine();
                    break;
                case XElement element when element.Name.LocalName.Equals("p", StringComparison.OrdinalIgnoreCase):
                    foreach (XNode child in element.Nodes())
                    {
                        Walk(child);
                    }

                    AddLine();
                    break;
                case XElement element:
                    foreach (XNode child in element.Nodes())
                    {
                        Walk(child);
                    }
                    break;
            }
        }

        foreach (XNode node in foreignObject.Nodes())
        {
            Walk(node);
        }

        AddLine();
        return lines;
    }

    private static double ReadSvgDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0d;
        }

        string numeric = Regex.Match(value, @"-?\d+(\.\d+)?").Value;
        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 0d;
    }

    private static string FormatSvgDouble(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static int? GetImageWidth(string filePath)
    {
        try
        {
            using SKBitmap bitmap = SKBitmap.Decode(filePath);
            return bitmap.Width > 0 ? Math.Min(bitmap.Width, 1200) : null;
        }
        catch
        {
            return null;
        }
    }
}
