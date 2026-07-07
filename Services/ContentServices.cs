using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;

namespace Portfolio.Services;

// ── Slug Servisi ──────────────────────────────────────────────────────────

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

            _ => false
        };
    }
}

// ── Okuma Süresi Servisi ──────────────────────────────────────────────────

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

// ── View Count Servisi ────────────────────────────────────────────────────

public interface IViewCountService
{
    Task IncrementAsync(string table, int id);
}

public class ViewCountService : IViewCountService
{
    private readonly AppDbContext _db;

    public ViewCountService(AppDbContext db) => _db = db;

    public async Task IncrementAsync(string table, int id)
    {
        var tableName = table switch
        {
            "Project" => "Projects",
            "SecurityResearch" => "SecurityResearches",
            "HomelabPost" => "HomelabPosts",
            "BlogPost" => "BlogPosts",
            "TeamProject" => "TeamProjects",
            _ => table
        };

        await _db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"{tableName}\" SET \"ViewCount\" = \"ViewCount\" + 1 WHERE \"Id\" = {{0}}", id
        );
    }
}