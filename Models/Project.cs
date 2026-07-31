using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Portfolio.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace Portfolio.Models
{
    public class Project
    {
        public int Id { get; set; }


        [Required(ErrorMessage = "Title is required.")]
        public string Title { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;           // unique index

        // Short description shown on listing pages (approximately 300 characters).
        public string Summary { get; set; } = string.Empty;

        // Full Markdown content rendered on the detail page.
        public string Content { get; set; } = string.Empty;

        public string? CoverImageUrl { get; set; }
        public string? LiveDemoUrl { get; set; }                   // Public demo address
        public string? GithubUrl { get; set; }
        public string? KnowledgeUrl { get; set; }                  // Public technical documentation

        // Category-specific JSON schema; see ExtraDataSchemas.cs.
        public string? ExtraData { get; set; }

        public bool IsFeatured { get; set; } = false;             // Feature on the homepage
        public int SortOrder { get; set; } = 0;                   // Featured-content order

        public VisibilityStatus Status { get; set; } = VisibilityStatus.Draft;

        // Calculated automatically by the reading-time service (word count / 200).
        public int ReadingTimeMinutes { get; set; } = 0;

        // Incremented with raw SQL by ViewCountService without EF tracking.
        public int ViewCount { get; set; } = 0;

        // Set when moving from Draft to Public and shown on listing pages.
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
