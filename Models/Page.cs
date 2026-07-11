using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Portfolio.Models.Enums;

namespace Portfolio.Models;

/// <summary>
/// Kod yazmadan admin panelinden eklenebilen basit statik sayfalar.
/// Hakkımda, SSS, Gizlilik Politikası gibi özel alan gerektirmeyen içerikler için.
/// </summary>
public class Page
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;          // /pages/{slug} ile erişilir

    public string Content { get; set; } = string.Empty;       // Markdown
    public string? CoverImageUrl { get; set; }

    public VisibilityStatus Status { get; set; } = VisibilityStatus.Draft;

    public bool ShowInNav { get; set; } = false;               // Navbar'da gösterilsin mi
    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}