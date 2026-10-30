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

using static PipelineTools.ConfluencePublisher.ConfluenceDocsPublisher;

sealed class ConfluenceClient : IDisposable
{
    private readonly string baseUrl;
    private readonly string spaceKey;
    private readonly bool dryRun;
    private readonly HttpClient httpClient = new();

    public ConfluenceClient(string baseUrl, string spaceKey, string email, string apiToken, bool dryRun)
    {
        this.baseUrl = baseUrl;
        this.spaceKey = spaceKey;
        this.dryRun = dryRun;

        if (!dryRun)
        {
            if (string.IsNullOrWhiteSpace(email) || email.Contains("$(", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("CONFLUENCE_EMAIL is required for Confluence publishing.");
            }

            if (string.IsNullOrWhiteSpace(apiToken) || apiToken.Contains("$(", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("CONFLUENCE_API_TOKEN is required for Confluence publishing.");
            }

            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{apiToken}"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> GetSpaceIdAsync(string spaceKey)
    {
        string query = QueryString(new Dictionary<string, string>
        {
            ["keys"] = spaceKey,
            ["limit"] = "25"
        });

        ConfluenceListResponse<ConfluenceSpace> response = await RequestJsonAsync<ConfluenceListResponse<ConfluenceSpace>>($"/api/v2/spaces?{query}");
        ConfluenceSpace? space = response.Results.FirstOrDefault(result => result.Key == spaceKey);

        if (space is null)
        {
            throw new InvalidOperationException($"Confluence space not found: {spaceKey}");
        }

        return space.Id;
    }

    public async Task<ConfluencePage?> FindPageAsync(string spaceId, string title, string parentId)
    {
        if (dryRun)
        {
            return null;
        }

        return await FindPageByFallbackLookupsAsync(title, parentId);
    }

    public async Task<string> ResolvePublishTitleAsync(string preferredTitle, string parentId, string fallbackTitle)
    {
        if (dryRun || string.Equals(preferredTitle, fallbackTitle, StringComparison.OrdinalIgnoreCase))
        {
            return preferredTitle;
        }

        List<ConfluencePage> preferredMatches = await FindPagesByTitleContentEndpointAsync(preferredTitle);
        if (preferredMatches.Any(page => page.ParentId == parentId))
        {
            return preferredTitle;
        }

        if (preferredMatches.Count == 0)
        {
            return preferredTitle;
        }

        string preferredParents = FormatParentIds(preferredMatches);
        Console.WriteLine($"Page title \"{preferredTitle}\" already exists outside expected parent {parentId}. Found parent id(s): {preferredParents}. Using \"{fallbackTitle}\" for this docs page.");

        List<ConfluencePage> fallbackMatches = await FindPagesByTitleContentEndpointAsync(fallbackTitle);
        if (fallbackMatches.Any(page => page.ParentId == parentId))
        {
            return fallbackTitle;
        }

        if (fallbackMatches.Count == 0)
        {
            return fallbackTitle;
        }

        throw new InvalidOperationException(
            $"Fallback page title \"{fallbackTitle}\" also exists outside expected parent {parentId}. " +
            "Set CONFLUENCE_PAGE_TITLE_PREFIX to make generated documentation titles unique.");
    }

    public async Task<ConfluencePage> UpsertPageAsync(
        string spaceId,
        string title,
        string parentId,
        string storage,
        ConfluencePage? existingPage = null)
    {
        if (dryRun)
        {
            return CreateDryRunPage(title, parentId, existingPage is null ? PublishAction.Created : PublishAction.Updated);
        }

        ConfluencePage? existing = existingPage ?? await FindPageAsync(spaceId, title, parentId);
        object body = CreateStorageBody(storage);

        if (existing is not null)
        {
            return await UpdatePageAsync(title, storage, existing);
        }

        Dictionary<string, object> createPayload = new()
        {
            ["type"] = "page",
            ["title"] = title,
            ["space"] = new { key = spaceKey },
            ["body"] = body
        };

        if (!string.IsNullOrWhiteSpace(parentId))
        {
            createPayload["ancestors"] = new[] { new { id = parentId } };
        }

        try
        {
            ConfluencePage created = await RequestJsonAsync<ConfluencePage>("/rest/api/content", HttpMethod.Post, createPayload);
            created.PublishAction = PublishAction.Created;
            return created;
        }
        catch (ConfluenceRequestException exception) when (exception.StatusCode == 400 && IsDuplicateTitleError(exception.ResponseText))
        {
            ConfluencePage? duplicate = await FindPageByFallbackLookupsAsync(title, parentId);
            if (duplicate is null)
            {
                throw new InvalidOperationException(
                    $"Confluence reported that page \"{title}\" already exists in space \"{spaceKey}\", but no page with that title exists under the expected parent {parentId}. " +
                    "The publisher will not update pages outside its configured docs tree. Rename the existing Confluence page or set CONFLUENCE_PAGE_TITLE_PREFIX to make generated documentation titles unique.",
                    exception);
            }

            Console.WriteLine($"Page title \"{title}\" already exists under expected parent {parentId}. Updating existing page {duplicate.Id}.");
            return await UpsertPageAsync(spaceId, title, parentId, storage, duplicate);
        }
    }

    private async Task<ConfluencePage> UpdatePageAsync(string title, string storage, ConfluencePage existing)
    {
        ConfluencePage pageForUpdate = existing.Version?.Number > 0
            ? existing
            : await GetPageAsync(existing.Id);

        int targetVersion = (pageForUpdate.Version?.Number ?? 1) + 1;

        try
        {
            return await UpdatePageWithVersionAsync(title, storage, pageForUpdate.Id, targetVersion);
        }
        catch (ConfluenceRequestException exception) when (exception.StatusCode == 409 || IsVersionConflictError(exception.ResponseText))
        {
            Console.WriteLine($"Version conflict detected for page \"{title}\". Refetching current version and retrying.");
            ConfluencePage refreshed = await GetPageAsync(pageForUpdate.Id);
            int refreshedVersion = (refreshed.Version?.Number ?? targetVersion) + 1;
            return await UpdatePageWithVersionAsync(title, storage, pageForUpdate.Id, refreshedVersion);
        }
    }

    private async Task<ConfluencePage> UpdatePageWithVersionAsync(string title, string storage, string pageId, int version)
    {
        object payload = new
        {
            id = pageId,
            type = "page",
            title,
            space = new { key = spaceKey },
            body = CreateStorageBody(storage),
            version = new { number = version }
        };

        ConfluencePage updated = await RequestJsonAsync<ConfluencePage>($"/rest/api/content/{Uri.EscapeDataString(pageId)}", HttpMethod.Put, payload);
        updated.PublishAction = PublishAction.Updated;
        return updated;
    }

    public async Task<ConfluenceAttachment> UploadAttachmentAsync(string pageId, string filePath, string fileName)
    {
        if (dryRun)
        {
            return new ConfluenceAttachment
            {
                Id = StableId($"{pageId}:{fileName}"),
                Title = fileName
            };
        }

        ConfluenceAttachment? existing = await GetAttachmentByFilenameAsync(pageId, fileName);
        string endpoint = existing is not null
            ? $"/rest/api/content/{Uri.EscapeDataString(pageId)}/child/attachment/{Uri.EscapeDataString(existing.Id)}/data"
            : $"/rest/api/content/{Uri.EscapeDataString(pageId)}/child/attachment";

        using MultipartFormDataContent form = new();
        byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
        ByteArrayContent fileContent = new(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(fileName));

        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent("true"), "minorEdit");
        form.Add(new StringContent("Published from repository Markdown"), "comment");

        await RequestRawAsync(endpoint, HttpMethod.Post, form, new Dictionary<string, string>
        {
            ["X-Atlassian-Token"] = "no-check"
        });

        ConfluenceAttachment? uploaded = await GetAttachmentByFilenameAsync(pageId, fileName);
        return uploaded ?? throw new InvalidOperationException($"Unable to resolve uploaded attachment {fileName} on page {pageId}");
    }

    private async Task<ConfluencePage> GetPageAsync(string pageId)
    {
        return await RequestJsonAsync<ConfluencePage>($"/rest/api/content/{Uri.EscapeDataString(pageId)}?expand=version,ancestors,_links");
    }

    private async Task<ConfluencePage?> FindPageByFallbackLookupsAsync(string title, string parentId)
    {
        ConfluencePage? contentEndpointMatch = await FindPageByContentEndpointAsync(title, parentId);
        return contentEndpointMatch ?? await FindPageByCqlAsync(title, parentId);
    }

    private async Task<ConfluencePage?> FindPageByContentEndpointAsync(string title, string parentId)
    {
        List<ConfluencePage> matches = await FindPagesByTitleContentEndpointAsync(title);
        return ResolvePageMatchesForParent(title, parentId, matches);
    }

    private async Task<List<ConfluencePage>> FindPagesByTitleContentEndpointAsync(string title)
    {
        string query = QueryString(new Dictionary<string, string>
        {
            ["spaceKey"] = spaceKey,
            ["title"] = title,
            ["expand"] = "version,ancestors,_links",
            ["limit"] = "25"
        });

        ConfluenceContentSearchResponse response;
        try
        {
            response = await RequestJsonAsync<ConfluenceContentSearchResponse>($"/rest/api/content?{query}");
        }
        catch (ConfluenceRequestException exception) when (exception.StatusCode == 400)
        {
            Console.WriteLine($"Content lookup for page \"{title}\" failed: {exception.ResponseText[..Math.Min(exception.ResponseText.Length, 500)]}");
            return [];
        }

        Console.WriteLine($"Searching for page \"{title}\" in space \"{spaceKey}\" via content endpoint... found {response.Size} matches.");
        return response.Results
            .Where(content => string.Equals(content.Title, title, StringComparison.OrdinalIgnoreCase))
            .Select(MapContentToPage)
            .ToList();
    }

    private async Task<ConfluencePage?> FindPageByCqlAsync(string title, string parentId)
    {
        string cql = $"space = \"{EscapeCqlValue(spaceKey)}\" and title = \"{EscapeCqlValue(title)}\" and type = page and state = current";
        string query = QueryString(new Dictionary<string, string>
        {
            ["cql"] = cql,
            ["expand"] = "version,ancestors,_links",
            ["limit"] = "25"
        });

        ConfluenceContentSearchResponse response;
        try
        {
            response = await RequestJsonAsync<ConfluenceContentSearchResponse>($"/rest/api/content/search?{query}");
        }
        catch (ConfluenceRequestException exception) when (exception.StatusCode == 400)
        {
            Console.WriteLine($"CQL lookup for page \"{title}\" failed: {exception.ResponseText[..Math.Min(exception.ResponseText.Length, 500)]}");
            return null;
        }
        return ResolvePageMatchesForParent(
            title,
            parentId,
            response.Results
                .Where(content => string.Equals(content.Title, title, StringComparison.OrdinalIgnoreCase))
                .Select(MapContentToPage)
                .ToList());
    }

    private ConfluencePage? ResolvePageMatchesForParent(string title, string parentId, IReadOnlyCollection<ConfluencePage> matches)
    {
        ConfluencePage? parentMatch = matches.FirstOrDefault(page => page.ParentId == parentId);
        if (parentMatch is not null)
        {
            return parentMatch;
        }

        if (matches.Count > 0)
        {
            string parents = FormatParentIds(matches);
            Console.WriteLine($"Ignoring {matches.Count} page(s) named \"{title}\" because none are under expected parent {parentId}. Found parent id(s): {parents}.");
        }

        return null;
    }

    private static string FormatParentIds(IEnumerable<ConfluencePage> pages)
    {
        return string.Join(", ", pages
            .Select(page => string.IsNullOrWhiteSpace(page.ParentId) ? "<none>" : page.ParentId)
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static ConfluencePage MapContentToPage(ConfluenceContent content)
    {
        return new ConfluencePage
        {
            Id = content.Id,
            Title = content.Title,
            ParentId = content.Ancestors.LastOrDefault()?.Id,
            Version = content.Version,
            Links = content.Links
        };
    }

    private async Task<ConfluenceAttachment?> GetAttachmentByFilenameAsync(string pageId, string fileName)
    {
        string query = QueryString(new Dictionary<string, string>
        {
            ["filename"] = fileName,
            ["limit"] = "25"
        });

        ConfluenceListResponse<ConfluenceAttachment> response =
            await RequestJsonAsync<ConfluenceListResponse<ConfluenceAttachment>>($"/api/v2/pages/{Uri.EscapeDataString(pageId)}/attachments?{query}");

        return response.Results.FirstOrDefault(attachment => attachment.Title == fileName);
    }

    private async Task<T> RequestJsonAsync<T>(string route, HttpMethod? method = null, object? body = null)
    {
        using HttpRequestMessage request = new(method ?? HttpMethod.Get, $"{baseUrl}{route}");

        if (body is not null)
        {
            string json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new ConfluenceRequestException(
                $"{request.Method} {route} failed with {(int)response.StatusCode}: {text[..Math.Min(text.Length, 2000)]}",
                (int)response.StatusCode,
                text);
        }

        return string.IsNullOrWhiteSpace(text)
            ? Activator.CreateInstance<T>()
            : JsonSerializer.Deserialize<T>(text, JsonOptions) ?? Activator.CreateInstance<T>();
    }

    private async Task RequestRawAsync(string route, HttpMethod method, HttpContent content, IReadOnlyDictionary<string, string>? headers = null)
    {
        using HttpRequestMessage request = new(method, $"{baseUrl}{route}")
        {
            Content = content
        };

        if (headers is not null)
        {
            foreach ((string key, string value) in headers)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request);
        string text = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new ConfluenceRequestException(
                $"{request.Method} {route} failed with {(int)response.StatusCode}: {text[..Math.Min(text.Length, 2000)]}",
                (int)response.StatusCode,
                text);
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private static bool IsDuplicateTitleError(string responseText)
    {
        return responseText.Contains("same TITLE", StringComparison.OrdinalIgnoreCase) ||
               responseText.Contains("page with this title already exists", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionConflictError(string responseText)
    {
        return responseText.Contains("Version must be incremented", StringComparison.OrdinalIgnoreCase) ||
               responseText.Contains("conflict", StringComparison.OrdinalIgnoreCase);
    }

    private static object CreateStorageBody(string storage)
    {
        return new
        {
            storage = new
            {
                value = storage,
                representation = "storage"
            }
        };
    }

    private static string EscapeCqlValue(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
