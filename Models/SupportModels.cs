using Portfolio.Models.Enums;

namespace Portfolio.Models
{
    public class Media
    {
        public int Id { get; set; }

        // "project" | "security_research" | "homelab_post" | "blog_post" | "team_project"
        public string EntityType { get; set; } = string.Empty;
        public int EntityId { get; set; }

        public string Url { get; set; } = string.Empty;           // Tam URL veya /uploads/... yolu
        public string Filename { get; set; } = string.Empty;      // Orijinal dosya adı
        public string? AltText { get; set; }                      // SEO + erişilebilirlik
        public string? Caption { get; set; }                      // "ESP32'nin Wi-Fi çipi"
        public string? MimeType { get; set; }                     // "image/webp"
        public long? FileSizeBytes { get; set; }
        public int? WidthPx { get; set; }                        // Responsive img için
        public int? HeightPx { get; set; }

        public int SortOrder { get; set; } = 0;
        public bool IsCover { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Etiketler ─────────────────────────────────────────────────────────────

    public class Tag
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;          // "ESP32", "Malware", "pfSense"
        public string Slug { get; set; } = string.Empty;          // "esp32", "malware"
        public string? ColorHex { get; set; }                     // "#5B4A8A" — UI'da badge rengi

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Many-to-many — UsingEntity("Taggables") ile yönetilir
        public ICollection<Project> Projects { get; set; } = new List<Project>();
        public ICollection<SecurityResearch> SecurityResearches { get; set; } = new List<SecurityResearch>();
        public ICollection<HomelabPost> HomelabPosts { get; set; } = new List<HomelabPost>();
        public ICollection<BlogPost> BlogPosts { get; set; } = new List<BlogPost>();
        public ICollection<TeamProject> TeamProjects { get; set; } = new List<TeamProject>();
    }

    // ── Hizmetler ─────────────────────────────────────────────────────────────

    public class Service
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;         // "Ev Ağı Güvenlik Testi"
        public string Description { get; set; } = string.Empty;
        public string? IconClass { get; set; }

        public VisibilityStatus Status { get; set; } = VisibilityStatus.Public;
        public int SortOrder { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ServiceReference> References { get; set; } = new List<ServiceReference>();
        public ICollection<ContactMessage> ContactMessages { get; set; } = new List<ContactMessage>();
    }

    /// <summary>
    /// Bir hizmeti ilgili içerik çalışmalarına bağlar.
    /// Polimorfik: RefType + RefId → hangi içerik.
    /// Bileşik PK: (ServiceId, RefType, RefId)
    /// </summary>
    public class ServiceReference
    {
        public int ServiceId { get; set; }
        public Service Service { get; set; } = null!;

        public string RefType { get; set; } = string.Empty;
        public int RefId { get; set; }

        public int DisplayOrder { get; set; } = 0;
        public string? CustomLabel { get; set; }                   
    }

    // ── İletişim ──────────────────────────────────────────────────────────────

    public class ContactMessage
    {
        public int Id { get; set; }
        public Guid TicketNumber { get; set; } = Guid.NewGuid();  

        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string Message { get; set; } = string.Empty;

        // Hangi hizmetle ilgileniyor — FK, nullable
        public int? ServiceId { get; set; }
        public Service? Service { get; set; }

        public string? IpAddress { get; set; }                    // Spam kontrolü (IPv6: max 45 char)
        public string? UserAgent { get; set; }

        public bool IsRead { get; set; } = false;
        public ContactStatus Status { get; set; } = ContactStatus.New;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Audit Log ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Admin panelindeki tüm değişiklikler.
    /// AuditService tarafından SaveChanges() override'ında otomatik doldurulur.
    /// </summary>
    public class AuditLog
    {
        public long Id { get; set; }                              // BIGSERIAL — büyük tablo

        public AuditAction Action { get; set; }
        public string EntityType { get; set; } = string.Empty;   // "Project", "Category" vs.
        public int EntityId { get; set; }
        public string? EntityTitle { get; set; }                  // Silinen kayıtlar için başlık snapshot'ı

        public string? OldValues { get; set; }                   // JSON — değişmeden önceki hali
        public string? NewValues { get; set; }                   // JSON — değişmeden sonraki hali

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SiteSettings
    {
        public int Id { get; set; }
        public string? CvFileUrl { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}