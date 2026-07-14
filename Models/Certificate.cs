using Portfolio.Models.Enums;

namespace Portfolio.Models;

public class Certificate
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;          // "CompTIA Security+"
    public string Issuer { get; set; } = string.Empty;         // "CompTIA"
    public string? CredentialId { get; set; }                  // Certificate number
    public string? CredentialUrl { get; set; }                 // Verification link
    public string? ImageUrl { get; set; }                       // Badge image

    public DateOnly IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }                  // Null for certificates that do not expire

    public VisibilityStatus Status { get; set; } = VisibilityStatus.Public;
    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
