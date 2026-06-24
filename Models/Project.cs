using Portfolio.Models.Enums;

namespace Portfolio.Models
{
    public class Project
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;           // unique index

        // Liste sayfasında görünen kısa açıklama (~300 karakter)
        public string Summary { get; set; } = string.Empty;

        // Detay sayfasında render edilen tam Markdown içerik
        public string Content { get; set; } = string.Empty;

        public string? CoverImageUrl { get; set; }
        public string? LiveDemoUrl { get; set; }                   // subdomain linki
        public string? GithubUrl { get; set; }

        // Kategoriye göre farklı JSON şeması — bkz. ExtraDataSchemas.cs
        public string? ExtraData { get; set; }

        public bool IsFeatured { get; set; } = false;             // Ana sayfada öne çıkar
        public int SortOrder { get; set; } = 0;                   // Featured sıralaması

        public VisibilityStatus Status { get; set; } = VisibilityStatus.Draft;

        // SlugService tarafından otomatik hesaplanır (kelime sayısı / 200)
        public int ReadingTimeMinutes { get; set; } = 0;

        // ViewCountService ile raw SQL ile artırılır (EF tracking olmadan)
        public int ViewCount { get; set; } = 0;

        // Draft → Public geçişinde set edilir, liste sayfasında görünür
        public DateTime? PublishedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // FK
        public int CategoryId { get; set; }
        public Category Category { get; set; } = null!;

        // Navigation
        public ICollection<Media> Images { get; set; } = new List<Media>();
        public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    }
}
