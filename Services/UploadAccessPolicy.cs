using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.ExtraData;
using System.Text.Json;

namespace Portfolio.Services;

/// <summary>Current content ownership, not knowledge of a URL, authorizes uploaded files.</summary>
public sealed class UploadAccessPolicy(AppDbContext db, IConfiguration configuration)
{
    // Ownership must not vary with an attacker-controlled request Host header.
    private readonly string[] publicAuthorities = GetPublicAuthorities(configuration);
    private static readonly HashSet<string> OwnedTypes =
        ["project", "security_research", "homelab_post", "homelab_topology", "blog_post", "team_project"];
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions().DisableHtml().Build();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<bool> CanReadAsync(string path, CancellationToken ct)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // The upload writer's namespace is authoritative. A public link on another page
        // cannot make a private owner's file public, even after the media row is deleted.
        if (segments.Length >= 2 && OwnedTypes.Contains(segments[1].ToLowerInvariant()))
        {
            if (segments.Length != 4 || !int.TryParse(segments[2], out var id) || id <= 0)
                return false;
            var type = segments[1].ToLowerInvariant();
            var media = await db.Media.AsNoTracking()
                .Where(m => m.EntityType == type && m.EntityId == id).ToListAsync(ct);
            if (!media.Any(m => RefersTo(m.Url, path)))
                return false;
            if (!await IsPublicAsync(type, id, ct))
                return false;
            if (type != "homelab_topology")
                return true;
            var topology = await db.HomelabPosts.Where(h => h.Id == id)
                .Select(h => h.NetworkTopology).SingleOrDefaultAsync(ct);
            return TopologyRefersTo(topology, path);
        }

        if (string.Equals(path, "/uploads/cv/cv.pdf", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            var cv = await db.SiteSettings.OrderBy(s => s.Id).Select(s => s.CvFileUrl).FirstOrDefaultAsync(ct);
            return RefersTo(cv, path);
        }

        // Legacy files may predate Media ownership (topology, manual covers, diagrams,
        // schematics and Markdown). Inspect their real references, not just a filename
        // substring. Any restricted owner blocks a shared legacy asset conservatively.
        var owners = new HashSet<(string Type, int Id)>();
        foreach (var media in await db.Media.AsNoTracking().ToListAsync(ct))
            if (RefersTo(media.Url, path)) owners.Add((media.EntityType, media.EntityId));

        foreach (var p in await db.Projects.IgnoreQueryFilters().AsNoTracking()
                     .Select(p => new { p.Id, p.CoverImageUrl, p.Content, p.ExtraData }).ToListAsync(ct))
        {
            var schematic = GetSchematicUrl(p.ExtraData);
            if (RefersTo(p.CoverImageUrl, path) || RefersTo(schematic, path) || MarkdownRefersTo(p.Content, path))
                owners.Add(("project", p.Id));
        }
        foreach (var p in await db.SecurityResearches.IgnoreQueryFilters().AsNoTracking()
                     .Select(p => new { p.Id, p.CoverImageUrl, p.Content }).ToListAsync(ct))
            if (RefersTo(p.CoverImageUrl, path) || MarkdownRefersTo(p.Content, path)) owners.Add(("security_research", p.Id));
        foreach (var p in await db.HomelabPosts.IgnoreQueryFilters().AsNoTracking()
                     .Select(p => new { p.Id, p.CoverImageUrl, p.Content, p.NetworkDiagramUrl, p.NetworkTopology }).ToListAsync(ct))
            if (RefersTo(p.CoverImageUrl, path) || RefersTo(p.NetworkDiagramUrl, path) ||
                TopologyRefersTo(p.NetworkTopology, path) || MarkdownRefersTo(p.Content, path)) owners.Add(("homelab_post", p.Id));
        foreach (var p in await db.BlogPosts.IgnoreQueryFilters().AsNoTracking()
                     .Select(p => new { p.Id, p.CoverImageUrl, p.Content }).ToListAsync(ct))
            if (RefersTo(p.CoverImageUrl, path) || MarkdownRefersTo(p.Content, path)) owners.Add(("blog_post", p.Id));
        foreach (var p in await db.TeamProjects.IgnoreQueryFilters().AsNoTracking()
                     .Select(p => new { p.Id, p.CoverImageUrl, p.Content }).ToListAsync(ct))
            if (RefersTo(p.CoverImageUrl, path) || MarkdownRefersTo(p.Content, path)) owners.Add(("team_project", p.Id));
        foreach (var p in await db.Pages.IgnoreQueryFilters().AsNoTracking()
                     .Select(p => new { p.Id, p.CoverImageUrl, p.Content }).ToListAsync(ct))
            if (RefersTo(p.CoverImageUrl, path) || MarkdownRefersTo(p.Content, path)) owners.Add(("page", p.Id));
        foreach (var p in await db.Certificates.IgnoreQueryFilters().AsNoTracking()
                     .Select(p => new { p.Id, p.ImageUrl }).ToListAsync(ct))
            if (RefersTo(p.ImageUrl, path)) owners.Add(("certificate", p.Id));

        if (owners.Count == 0) return false;
        foreach (var owner in owners)
            if (!await IsPublicAsync(owner.Type, owner.Id, ct)) return false;
        return true;
    }

    // Use the application's existing query filters: they include category visibility
    // and disclosure. Do not duplicate, cache, or bypass those rules here.
    private Task<bool> IsPublicAsync(string type, int id, CancellationToken ct) => type switch
    {
        "project" => db.Projects.AnyAsync(p => p.Id == id, ct),
        "security_research" => db.SecurityResearches.AnyAsync(p => p.Id == id, ct),
        "homelab_post" or "homelab_topology" => db.HomelabPosts.AnyAsync(p => p.Id == id, ct),
        "blog_post" => db.BlogPosts.AnyAsync(p => p.Id == id, ct),
        "team_project" => db.TeamProjects.AnyAsync(p => p.Id == id, ct),
        "page" => db.Pages.AnyAsync(p => p.Id == id, ct),
        "certificate" => db.Certificates.AnyAsync(p => p.Id == id, ct),
        _ => Task.FromResult(false)
    };

    private static string? GetSchematicUrl(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<ElectronicsExtraData>(json, JsonOptions)?.SchematicUrl; }
        catch (JsonException) { return null; }
    }

    private bool TopologyRefersTo(string? json, string path) =>
        NetworkTopologyJsonService.TryNormalize(json, out var topology, out _) &&
        topology?.Nodes.Any(n => RefersTo(n.IconUrl, path) || RefersTo(n.StandaloneImageUrl, path)) == true;

    private bool MarkdownRefersTo(string? markdown, string path) =>
        !string.IsNullOrEmpty(markdown) && Markdown.Parse(markdown, MarkdownPipeline)
            .Descendants<LinkInline>().Any(link => SafeUrlPolicy.IsSafeNavigationUrl(link.Url) && RefersTo(link.Url, path));

    private bool RefersTo(string? url, string path) =>
        UploadPath.TryNormalizeReference(url, publicAuthorities, out var reference) &&
        string.Equals(reference, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string[] GetPublicAuthorities(IConfiguration config)
    {
        var origins = config.GetSection("MediaStorage:PublicOrigins").Get<string[]>() ??
            ["https://emecworks.com", "https://www.emecworks.com"];
        return origins.Select(origin =>
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
                throw new InvalidOperationException("MediaStorage:PublicOrigins must contain explicit HTTPS origins only.");
            return uri.Authority;
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public static class UploadPath
{
    public static bool IsUploadRequest(PathString path)
    {
        var value = path.Value ?? "";
        // Claim alternate separators/encodings too; they must not fall through to
        // Windows static-file interpretation without the upload guard.
        return value.TrimStart('/').StartsWith("uploads", StringComparison.OrdinalIgnoreCase) ||
               Uri.UnescapeDataString(value).Replace('\\', '/').TrimStart('/').StartsWith("uploads", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryNormalizeReference(string? url, IReadOnlyCollection<string> publicAuthorities, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 || url.Contains('\\') || url.Any(char.IsControl)) return false;
        var value = url.Trim();
        Uri? uri;
        var isAbsolute = Uri.TryCreate(value, UriKind.Absolute, out var absolute);
        if (!value.StartsWith("//") && (value.StartsWith('/') || !isAbsolute))
        {
            // Public detail routes are one directory deep (/blog/{slug}, /pages/{slug},
            // etc.). Match the browser's resolution of ../uploads and dot segments.
            if (!Uri.TryCreate(new Uri("https://upload-reference.invalid/content/"), value, out uri)) return false;
        }
        else
        {
            uri = absolute;
            if (uri == null || (uri.Scheme != "https" && uri.Scheme != "http") ||
                uri.UserInfo.Length != 0 || !publicAuthorities.Contains(uri.Authority, StringComparer.OrdinalIgnoreCase)) return false;
        }
        path = Uri.UnescapeDataString(uri.AbsolutePath);
        return IsCanonicalPath(path);
    }

    public static bool IsCanonicalPath(string path) =>
        path.Length <= 2048 && path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) &&
        !path.Any(char.IsControl) && !path.Contains('\\') && !path.Contains('%') && !path.Contains(':') &&
        !path.Split('/').Skip(1).Any(s => s.Length == 0 || s is "." or ".." || s.EndsWith('.') || s.EndsWith(' '));
}
