using Portfolio.Models.Enums;

namespace Portfolio.Models
{
    public class Category
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;          // "Elektronik & Mikrodenetleyiciler"
        public string Slug { get; set; } = string.Empty;          // "electronics" — unique index ile
        public string? IconClass { get; set; }                    // "ti ti-cpu" (Tabler Icons)
        public string? Description { get; set; }                  // Bölüm sayfasındaki hero text

        // VisibilityStatus.Public → bölüm görünür
        // VisibilityStatus.Draft  → bölüm gizli (maintenance)
        public VisibilityStatus Status { get; set; } = VisibilityStatus.Public;

        // True ise URL'den bile erişilemez, 404 döner (notlar gibi özel bölümler)
        public bool IsPrivate { get; set; } = false;

        public int SortOrder { get; set; } = 0;                   // Menü sıralaması

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Project> Projects { get; set; } = new List<Project>();
        public ICollection<SecurityResearch> SecurityResearches { get; set; } = new List<SecurityResearch>();
        public ICollection<HomelabPost> HomelabPosts { get; set; } = new List<HomelabPost>();
        public ICollection<BlogPost> BlogPosts { get; set; } = new List<BlogPost>();
        public ICollection<TeamProject> TeamProjects { get; set; } = new List<TeamProject>();
    }
}
