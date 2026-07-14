using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Portfolio.Models.Enums;

namespace Portfolio.Models;

/// <summary>
/// Simple static pages that can be added from the admin panel without changing code.
/// Content that does not require custom fields, such as About, FAQ, or Privacy Policy pages.
/// </summary>
public class Page
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;          // Accessible at /pages/{slug}

    public string Content { get; set; } = string.Empty;       // Markdown
    public string? CoverImageUrl { get; set; }

    public VisibilityStatus Status { get; set; } = VisibilityStatus.Draft;

    public bool ShowInNav { get; set; } = false;               // Show in the main navigation
    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
