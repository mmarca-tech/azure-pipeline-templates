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
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .UseListExtras()
        .UseTaskLists()
        .Build();

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task RunAsync()
    {
        string docsRoot = Path.GetFullPath(GetEnv("DOCS_ROOT", "src/docs"));
        string workDir = Path.GetFullPath(GetEnv("CONFLUENCE_WORK_DIR", Path.Combine(Environment.CurrentDirectory, ".confluence-docs-work")));
        bool dryRun = IsTrue(GetEnv("CONFLUENCE_DRY_RUN", "false"));
        string baseUrl = TrimTrailingSlash(GetEnv("CONFLUENCE_BASE_URL", "https://your-domain.atlassian.net/wiki"));
        string spaceKey = RequiredEnv("CONFLUENCE_SPACE_KEY");
        string parentPageId = RequiredEnv("CONFLUENCE_PARENT_PAGE_ID");
        string titlePrefix = GetEnv("CONFLUENCE_PAGE_TITLE_PREFIX", string.Empty);

        if (!Directory.Exists(docsRoot))
        {
            throw new InvalidOperationException($"Docs root does not exist: {docsRoot}");
        }

        using ConfluenceClient client = new(
            baseUrl,
            spaceKey,
            GetEnv("CONFLUENCE_EMAIL", string.Empty),
            GetEnv("CONFLUENCE_API_TOKEN", string.Empty),
            dryRun);

        Directory.CreateDirectory(workDir);

        List<string> markdownFiles = CollectMarkdownFiles(docsRoot);
        if (markdownFiles.Count == 0)
        {
            Console.WriteLine($"No Markdown files found under {docsRoot}");
            return;
        }

        List<DocsGroup> groups = GroupByDocsSubfolder(docsRoot, markdownFiles);
        string spaceId = spaceKey;

        Console.WriteLine($"Publishing {markdownFiles.Count} Markdown file(s) from {docsRoot} to Confluence space {spaceKey}");
        Console.WriteLine("Format: Markdown -> Confluence Storage Format");
        Console.WriteLine($"Parent page: {parentPageId}");

        if (dryRun)
        {
            Console.WriteLine("Dry run enabled. No Confluence API writes will be made.");
        }

        foreach (DocsGroup group in groups)
        {
            string folderTitle = $"{titlePrefix}{ToTitleCase(group.Name)}";
            ConfluencePage folderPage = await client.UpsertPageAsync(
                spaceId,
                folderTitle,
                parentPageId,
                CreateIndexStorage(folderTitle, []));

            List<ChildPageLink> children = [];

            foreach (string markdownFile in group.Files)
            {
                string source = await File.ReadAllTextAsync(markdownFile);
                string markdownTitle = GetMarkdownTitle(source, markdownFile);
                string preferredPageTitle = $"{titlePrefix}{markdownTitle}";
                string fallbackPageTitle = $"{folderTitle} - {markdownTitle}";
                string pageTitle = await client.ResolvePublishTitleAsync(preferredPageTitle, folderPage.Id, fallbackPageTitle);
                List<ImageReference> imageRefs = CollectImageReferences(source, markdownFile);

                ConfluencePage? existingPage = await client.FindPageAsync(spaceId, pageTitle, folderPage.Id);
                bool createdPlaceholder = false;

                if (imageRefs.Count > 0 && existingPage is null)
                {
                    existingPage = await client.UpsertPageAsync(
                        spaceId,
                        pageTitle,
                        folderPage.Id,
                        CreatePlaceholderStorage(pageTitle));
                    createdPlaceholder = true;
                }

                Dictionary<string, PreparedImage> imageMap = await PrepareImagesAsync(
                    imageRefs,
                    existingPage?.Id,
                    markdownFile,
                    workDir,
                    client);

                string storage = MarkdownToStorage(source, markdownFile, imageMap);
                await WriteStorageSnapshotAsync(workDir, group.Name, pageTitle, storage);

                ConfluencePage page = await client.UpsertPageAsync(
                    spaceId,
                    pageTitle,
                    folderPage.Id,
                    storage,
                    existingPage);

                string pageAction = createdPlaceholder ? "Created" : FormatPublishAction(page.PublishAction);
                children.Add(new ChildPageLink(pageTitle, AbsoluteWebUrl(baseUrl, page)));

                Console.WriteLine($"{pageAction} page: {folderTitle} / {pageTitle}");
            }

            string folderIndexStorage = CreateIndexStorage(folderTitle, children);
            await WriteStorageSnapshotAsync(workDir, group.Name, folderTitle, folderIndexStorage);

            ConfluencePage updatedFolderPage = await client.UpsertPageAsync(
                spaceId,
                folderTitle,
                parentPageId,
                folderIndexStorage,
                folderPage);

            string folderAction = folderPage.PublishAction == PublishAction.Created
                ? "Created"
                : FormatPublishAction(updatedFolderPage.PublishAction);

            Console.WriteLine($"{folderAction} folder page: {folderTitle}");
        }
    }

    private static async Task<Dictionary<string, PreparedImage>> PrepareImagesAsync(
        IEnumerable<ImageReference> imageRefs,
        string? pageId,
        string markdownFile,
        string workDir,
        ConfluenceClient client)
    {
        Dictionary<string, PreparedImage> images = new(StringComparer.OrdinalIgnoreCase);

        foreach (ImageReference imageRef in imageRefs)
        {
            if (imageRef.LocalPath is null)
            {
                continue;
            }

            if (pageId is null)
            {
                throw new InvalidOperationException($"Image references require an existing Confluence page before upload: {markdownFile}");
            }

            if (!File.Exists(imageRef.LocalPath))
            {
                throw new FileNotFoundException($"Markdown image not found: {imageRef.Source} in {markdownFile}", imageRef.LocalPath);
            }

            PreparedImageFile prepared = await PrepareImageFileAsync(imageRef.LocalPath, pageId, workDir);
            int? width = GetImageWidth(prepared.FilePath);
            ConfluenceAttachment attachment = await client.UploadAttachmentAsync(pageId, prepared.FilePath, prepared.FileName);

            images[imageRef.Key] = new PreparedImage(
                attachment.Title ?? prepared.FileName,
                string.IsNullOrWhiteSpace(imageRef.Alt) ? prepared.FileName : imageRef.Alt,
                width);
        }

        return images;
    }
}
