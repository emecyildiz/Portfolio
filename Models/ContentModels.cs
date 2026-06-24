using Portfolio.Models.Enums;

namespace Portfolio.Models
{
    public class ContentModels
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;

        public HomelabTopic Topic { get; set; }

        public string Summary { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;       // Markdown

        // ["Raspberry Pi 4 8GB", "pfSense mini PC"] — JSONB
        public string? HardwareUsed { get; set; }

        // ["pfSense 2.7", "Snort 3", "Grafana"] — JSONB
        public string? SoftwareUsed { get; set; }

        // Ağ topoloji diyagramı gibi büyük görseller için ayrı alan
        public string? NetworkDiagramUrl { get; set; }
        public string? CoverImageUrl { get; set; }

        public VisibilityStatus Status { get; set; } = VisibilityStatus.Draft;
        public bool IsFeatured { get; set; } = false;

        public int ReadingTimeMinutes { get; set; } = 0;
        public int ViewCount { get; set; } = 0;

        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public int CategoryId { get; set; }
        public Category Category { get; set; } = null!;

        public ICollection<Media> Images { get; set; } = new List<Media>();
        public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    }


    public class BlogPost
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;

        public string? CoverImageUrl { get; set; }

        public VisibilityStatus Status { get; set; } = VisibilityStatus.Draft;
        public bool IsFeatured { get; set; } = false;

        public int ReadingTimeMinutes { get; set; } = 0;
        public int ViewCount { get; set; } = 0;

        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public int CategoryId { get; set; }
        public Category Category { get; set; } = null!;

        public ICollection<Media> Images { get; set; } = new List<Media>();
        public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    }


    public class TeamProject
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;

        public string? EventName { get; set; }                    // "Teknofest 2024"
        public DateOnly? EventDate { get; set; }
        public string? EventUrl { get; set; }

        public string Summary { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;

        public string MyRole { get; set; } = string.Empty;       // "Backend Dev & Security"

        // [{ name, role, github_url, linkedin_url }] — JSONB
        // Deserialize: JsonSerializer.Deserialize<List<TeamMember>>(TeamMembers)
        public string? TeamMembers { get; set; }

        public string? Outcome { get; set; }                     // "2. Ödül", "Tamamlandı"
        public string? CoverImageUrl { get; set; }
        public string? GithubUrl { get; set; }
        public string? LiveDemoUrl { get; set; }

        public VisibilityStatus Status { get; set; } = VisibilityStatus.Draft;
        public bool IsFeatured { get; set; } = false;

        public int ViewCount { get; set; } = 0;

        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public int CategoryId { get; set; }
        public Category Category { get; set; } = null!;

        public ICollection<Media> Images { get; set; } = new List<Media>();
        public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    }
}
