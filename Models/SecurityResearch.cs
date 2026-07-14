using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Portfolio.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Portfolio.Models
{
    public class SecurityResearch
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Title is required.")]
        public string Title { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;          // unique index

        public ResearchType ResearchType { get; set; }
        public string? TargetCategory { get; set; }               // "Automotive", "IoT", "Mobile"
        public string? CveId { get; set; }                        // "CVE-2024-XXXXX"

        // critical / high / medium / low / info
        // Stored as a string to keep the field flexible instead of restricting it to an enum.
        public string? SeverityLevel { get; set; }

        public string Summary { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;       // Markdown write-up

        // ["Ghidra", "Wireshark", "HackRF", "SDR++"] — JSONB
        // Read with JsonSerializer.Deserialize<List<string>>(ToolsUsed).
        public string? ToolsUsed { get; set; }

        public string? CoverImageUrl { get; set; }
        public string? GithubUrl { get; set; }                    // PoC kodu

        // Critical: research cannot reach the public endpoint unless it is PubliclyDisclosed.
        public DisclosureStatus DisclosureStatus { get; set; } = DisclosureStatus.Private;

        public VisibilityStatus Status { get; set; } = VisibilityStatus.Draft;
        public bool IsFeatured { get; set; } = false;

        public int ReadingTimeMinutes { get; set; } = 0;
        public int ViewCount { get; set; } = 0;

        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // FK
        public int CategoryId { get; set; }
        [ValidateNever]
        public Category Category { get; set; } = null!;

        // Navigation
        public ICollection<Media> Images { get; set; } = new List<Media>();
        public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    }
}
