using Portfolio.Models.Enums;

namespace Portfolio.Models;

public class Certificate
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;          // "CompTIA Security+"
    public string Issuer { get; set; } = string.Empty;         // "CompTIA"
    public string? CredentialId { get; set; }                  // Sertifika numarası
    public string? CredentialUrl { get; set; }                 // Doğrulama linki
    public string? ImageUrl { get; set; }                       // Rozet görseli

    public DateOnly IssuedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }                  // Süresi dolmayan sertifikalar için null

    public VisibilityStatus Status { get; set; } = VisibilityStatus.Public;
    public int SortOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}