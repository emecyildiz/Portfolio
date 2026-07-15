using Portfolio.Models.Enums;

namespace Portfolio.Models
{
    public class Media
    {
        public int Id { get; set; }

        // "project" | "security_research" | "homelab_post" | "blog_post" | "team_project"
        public string EntityType { get; set; } = string.Empty;
        public int EntityId { get; set; }

        public string Url { get; set; } = string.Empty;           // Absolute URL or /uploads/... path
        public string Filename { get; set; } = string.Empty;      // Original filename
        public string? AltText { get; set; }                      // SEO and accessibility text
        public string? Caption { get; set; }                      // "ESP32 Wi-Fi chip"
        public string? MimeType { get; set; }                     // "image/webp"
        public long? FileSizeBytes { get; set; }
        public int? WidthPx { get; set; }                        // Used for responsive images
        public int? HeightPx { get; set; }

        public int SortOrder { get; set; } = 0;
        public bool IsCover { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Tags ──────────────────────────────────────────────────────────────────

    public class Tag
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;          // "ESP32", "Malware", "pfSense"
        public string Slug { get; set; } = string.Empty;          // "esp32", "malware"
        public string? ColorHex { get; set; }                     // "#5B4A8A" — UI'da badge rengi

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Many-to-many relationship managed through UsingEntity("Taggables").
        public ICollection<Project> Projects { get; set; } = new List<Project>();
        public ICollection<SecurityResearch> SecurityResearches { get; set; } = new List<SecurityResearch>();
        public ICollection<HomelabPost> HomelabPosts { get; set; } = new List<HomelabPost>();
        public ICollection<BlogPost> BlogPosts { get; set; } = new List<BlogPost>();
        public ICollection<TeamProject> TeamProjects { get; set; } = new List<TeamProject>();
    }

    // ── Services ──────────────────────────────────────────────────────────────

    public class Service
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;         // "Home Network Security Assessment"
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
    /// Links a service to related portfolio content.
    /// Polymorphic: RefType + RefId identifies the referenced content.
    /// Composite primary key: (ServiceId, RefType, RefId)
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

    // ── Contact ──────────────────────────────────────────────────────────────

    public class ContactMessage
    {
        public int Id { get; set; }
        public Guid TicketNumber { get; set; } = Guid.NewGuid();  

        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string Message { get; set; } = string.Empty;

        // Optional foreign key for the service of interest.
        public int? ServiceId { get; set; }
        public Service? Service { get; set; }

        public string? IpAddress { get; set; }                    // Spam checks (IPv6: maximum 45 characters)
        public string? UserAgent { get; set; }

        public bool IsRead { get; set; } = false;
        public ContactStatus Status { get; set; } = ContactStatus.New;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    // ── Audit Log ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Records changes made through the admin panel.
    /// Populated automatically by AuditService in the SaveChanges() override.
    /// </summary>
    public class AuditLog
    {
        public long Id { get; set; }                              // BIGSERIAL for a high-volume table

        public AuditAction Action { get; set; }
        public string EntityType { get; set; } = string.Empty;   // For example, "Project" or "Category"
        public int EntityId { get; set; }
        public string? EntityTitle { get; set; }                  // Title snapshot for deleted records

        public string? OldValues { get; set; }                   // JSON state before the change
        public string? NewValues { get; set; }                   // JSON state after the change

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SiteSettings
    {
        public int Id { get; set; }
        public string? CvFileUrl { get; set; }
        public string? FooterLinksJson { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
