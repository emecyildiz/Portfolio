using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace Portfolio.Models.Services
{
    public interface ISlugService
    {
        string Generate(string title);
        Task<string> GenerateUniqueAsync(string title, string table, int? excludeId = null);
    }

    public class SlugService : ISlugService
    {
        private readonly AppDbContext _db;

        public SlugService(AppDbContext db) => _db = db;

        /// <summary>
        /// Başlıktan URL-safe slug üretir.
        /// "ESP32 ile Wi-Fi Deauther!" → "esp32-ile-wi-fi-deauther"
        /// </summary>
        public string Generate(string title)
        {
            // Türkçe karakter normalizasyonu
            var normalized = title
                .ToLowerInvariant()
                .Replace('ş', 's').Replace('ğ', 'g').Replace('ı', 'i')
                .Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c')
                .Replace('Ş', 's').Replace('Ğ', 'g').Replace('İ', 'i')
                .Replace('Ü', 'u').Replace('Ö', 'o').Replace('Ç', 'c');

            // Boşlukları tire yap, özel karakterleri at
            var slug = Regex.Replace(normalized, @"[^a-z0-9\s-]", "");
            slug = Regex.Replace(slug, @"\s+", "-");
            slug = Regex.Replace(slug, @"-+", "-").Trim('-');

            return slug;
        }

        /// <summary>
        /// Slug çakışırsa -2, -3 ekler.
        /// "esp32-deauther" zaten varsa → "esp32-deauther-2"
        /// </summary>
        public async Task<string> GenerateUniqueAsync(string title, string table, int? excludeId = null)
        {
            var baseSlug = Generate(title);
            var slug = baseSlug;
            var counter = 2;

            while (await SlugExistsAsync(slug, table, excludeId))
            {
                slug = $"{baseSlug}-{counter++}";
            }

            return slug;
        }

        private async Task<bool> SlugExistsAsync(string slug, string table, int? excludeId)
        {
            // Her entity için ayrı kontrol — tablo adına göre switch yap
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

        /// <summary>
        /// Markdown içerikten kelime sayısı / 200 ile dakika hesaplar.
        /// Markdown syntax'ı temizleyip sadece metin kelimelerini sayar.
        /// </summary>
        public int Calculate(string markdownContent)
        {
            if (string.IsNullOrWhiteSpace(markdownContent))
                return 0;

            // Markdown syntax'ını temizle — başlık işaretleri, linkler, code block vs.
            var text = Regex.Replace(markdownContent, @"```[\s\S]*?```", " "); // code blocks
            text = Regex.Replace(text, @"`[^`]*`", " ");                       // inline code
            text = Regex.Replace(text, @"\[([^\]]*)\]\([^\)]*\)", "$1");       // linkler
            text = Regex.Replace(text, @"[#*_~>|]", " ");                      // markdown sembolleri
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

        /// <summary>
        /// EF Core tracking olmadan raw SQL ile view_count artırır.
        /// Performans kritik — her sayfa açılışında çalışır.
        /// </summary>
        public async Task IncrementAsync(string table, int id)
        {
            await _db.Database.ExecuteSqlRawAsync(
                $"UPDATE \"{table}\" SET view_count = view_count + 1 WHERE id = {{0}}", id
            );
        }
    }

}
