using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;

namespace Portfolio.Services;

// ── Slug Service ──────────────────────────────────────────────────────────

public interface ISlugService
{
    string Generate(string title);
    Task<string> GenerateUniqueAsync(string title, string table, int? excludeId = null);
}

public class SlugService : ISlugService
{
    private readonly AppDbContext _db;

    public SlugService(AppDbContext db) => _db = db;

    public string Generate(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var normalized = title
            .ToLowerInvariant()
            .Replace('ş', 's').Replace('ğ', 'g').Replace('ı', 'i')
            .Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c')
            .Replace('Ş', 's').Replace('Ğ', 'g').Replace('İ', 'i')
            .Replace('Ü', 'u').Replace('Ö', 'o').Replace('Ç', 'c');

        var slug = Regex.Replace(normalized, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-+", "-").Trim('-');

        return slug;
    }

    public async Task<string> GenerateUniqueAsync(string title, string table, int? excludeId = null)
    {
        var baseSlug = Generate(title);
        var slug = baseSlug;
        var counter = 2;

        while (await SlugExistsAsync(slug, table, excludeId))
            slug = $"{baseSlug}-{counter++}";

        return slug;
    }

    private async Task<bool> SlugExistsAsync(string slug, string table, int? excludeId)
    {
        return table switch
        {
            "Projects" => excludeId.HasValue
                ? await _db.Projects.IgnoreQueryFilters().AnyAsync(p => p.Slug == slug && p.Id != excludeId)
                : await _db.Projects.IgnoreQueryFilters().AnyAsync(p => p.Slug == slug),

            "SecurityResearches" => excludeId.HasValue
                ? await _db.SecurityResearches.IgnoreQueryFilters().AnyAsync(s => s.Slug == slug && s.Id != excludeId)
                : await _db.SecurityResearches.IgnoreQueryFilters().AnyAsync(s => s.Slug == slug),

            "HomelabPosts" => excludeId.HasValue
                ? await _db.HomelabPosts.IgnoreQueryFilters().AnyAsync(h => h.Slug == slug && h.Id != excludeId)
                : await _db.HomelabPosts.IgnoreQueryFilters().AnyAsync(h => h.Slug == slug),

            "BlogPosts" => excludeId.HasValue
                ? await _db.BlogPosts.IgnoreQueryFilters().AnyAsync(b => b.Slug == slug && b.Id != excludeId)
                : await _db.BlogPosts.IgnoreQueryFilters().AnyAsync(b => b.Slug == slug),

            "TeamProjects" => excludeId.HasValue
                ? await _db.TeamProjects.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug && t.Id != excludeId)
                : await _db.TeamProjects.IgnoreQueryFilters().AnyAsync(t => t.Slug == slug),

            "Pages" => excludeId.HasValue
                ? await _db.Pages.IgnoreQueryFilters().AnyAsync(p => p.Slug == slug && p.Id != excludeId)
                : await _db.Pages.IgnoreQueryFilters().AnyAsync(p => p.Slug == slug),

            _ => false
        };
    }
}

// ── Reading Time Service ──────────────────────────────────────────────────

public interface IReadingTimeService
{
    int Calculate(string markdownContent);
}

public class ReadingTimeService : IReadingTimeService
{
    private const int WordsPerMinute = 200;

    public int Calculate(string markdownContent)
    {
        if (string.IsNullOrWhiteSpace(markdownContent))
            return 0;

        var text = Regex.Replace(markdownContent, @"```[\s\S]*?```", " ");
        text = Regex.Replace(text, @"`[^`]*`", " ");
        text = Regex.Replace(text, @"\[([^\]]*)\]\([^\)]*\)", "$1");
        text = Regex.Replace(text, @"[#*_~>|]", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();

        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)Math.Ceiling((double)wordCount / WordsPerMinute);
    }
}

// ── View Count Service ────────────────────────────────────────────────────

public interface IViewCountService
{
    Task<bool> TryIncrementUniqueAsync(
        string table,
        int id,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}

public class ViewCountService : IViewCountService
{
    private const string VisitorProtectorPurpose = "Portfolio.ContentViews.Visitor.v1";

    private static readonly string[] BotUserAgentMarkers =
    [
        "bot", "crawler", "spider", "slurp", "facebookexternalhit", "whatsapp",
        "telegrambot", "discordbot", "twitterbot", "linkedinbot", "preview"
    ];

    private readonly AppDbContext _db;
    private readonly IDataProtector _visitorProtector;
    private readonly ILogger<ViewCountService> _logger;
    private readonly int _visitorCookieDays;

    public ViewCountService(
        AppDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration,
        ILogger<ViewCountService> logger)
    {
        _db = db;
        _visitorProtector = dataProtectionProvider.CreateProtector(VisitorProtectorPurpose);
        _logger = logger;
        _visitorCookieDays = configuration.GetValue("Privacy:ContentVisitorCookieDays", 90);

        if (_visitorCookieDays is < 30 or > 365)
        {
            throw new InvalidOperationException(
                "Privacy:ContentVisitorCookieDays must be between 30 and 365 days.");
        }
    }

    public async Task<bool> TryIncrementUniqueAsync(
        string table,
        int id,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldCountRequest(httpContext))
        {
            return false;
        }

        var (contentType, tableName) = table switch
        {
            "Project" or "Projects" => ("project", "Projects"),
            "SecurityResearch" or "SecurityResearches" => ("security", "SecurityResearches"),
            "HomelabPost" or "HomelabPosts" => ("homelab", "HomelabPosts"),
            "BlogPost" or "BlogPosts" => ("blog", "BlogPosts"),
            "TeamProject" or "TeamProjects" => ("team", "TeamProjects"),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Unsupported content table.")
        };

        var visitorId = GetOrCreateVisitorId(httpContext);
        var visitorHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(visitorId)))
            .ToLowerInvariant();
        var now = DateTime.UtcNow;
        var viewDate = DateOnly.FromDateTime(now);
        var sql = $$"""
            WITH target AS (
                SELECT "Id"
                FROM "{{tableName}}"
                WHERE "Id" = {1} AND "ViewCount" < 2147483647
            ),
            inserted AS (
                INSERT INTO "ContentViewReceipts"
                    ("ContentType", "ContentId", "VisitorHash", "ViewDate", "CreatedAt")
                SELECT {0}, {1}, {2}, {3}, {4}
                FROM target
                ON CONFLICT ("ContentType", "ContentId", "VisitorHash", "ViewDate")
                DO NOTHING
                RETURNING 1
            )
            UPDATE "{{tableName}}" AS content
            SET "ViewCount" = content."ViewCount" + 1
            WHERE content."Id" = {1}
              AND EXISTS (SELECT 1 FROM inserted)
            """;

        try
        {
            var affectedRows = await _db.Database.ExecuteSqlRawAsync(
                sql,
                [contentType, id, visitorHash, viewDate, now],
                cancellationToken);
            return affectedRows == 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unique content-view tracking failed for {ContentType} {ContentId}.",
                contentType,
                id);
            return false;
        }
    }

    private string GetOrCreateVisitorId(HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(AnalyticsConsent.VisitorCookieName, out var protectedVisitorId))
        {
            try
            {
                var visitorId = _visitorProtector.Unprotect(protectedVisitorId);
                if (Guid.TryParseExact(visitorId, "N", out _))
                {
                    return visitorId;
                }
            }
            catch (CryptographicException)
            {
                // Invalid or expired identifiers are replaced without trusting client input.
            }
        }

        var newVisitorId = Guid.NewGuid().ToString("N");
        httpContext.Response.Cookies.Append(
            AnalyticsConsent.VisitorCookieName,
            _visitorProtector.Protect(newVisitorId),
            new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(_visitorCookieDays),
                MaxAge = TimeSpan.FromDays(_visitorCookieDays),
                IsEssential = false
            });
        return newVisitorId;
    }

    private static bool ShouldCountRequest(HttpContext httpContext)
    {
        if (!AnalyticsConsent.IsGranted(httpContext) ||
            !HttpMethods.IsGet(httpContext.Request.Method) ||
            httpContext.User.Identity?.IsAuthenticated == true)
        {
            return false;
        }

        var accept = httpContext.Request.Headers.Accept.ToString();
        if (!accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var purpose = $"{httpContext.Request.Headers["Purpose"]} {httpContext.Request.Headers["Sec-Purpose"]}";
        if (purpose.Contains("prefetch", StringComparison.OrdinalIgnoreCase) ||
            purpose.Contains("preview", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var userAgent = httpContext.Request.Headers.UserAgent.ToString();
        return !string.IsNullOrWhiteSpace(userAgent) &&
               !BotUserAgentMarkers.Any(marker =>
                   userAgent.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

}
