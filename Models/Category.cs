using Portfolio.Models.Enums;

namespace Portfolio.Models
{
    public class Category
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;          // "Electronics & Microcontrollers"
        public string Slug { get; set; } = string.Empty;          // "electronics" — protected by a unique index
        public string? IconClass { get; set; }                    // "ti ti-cpu" (Tabler Icons)
        public string? Description { get; set; }                  // Hero text on the section page

        // VisibilityStatus.Public → section is visible
        // VisibilityStatus.Draft  → section is hidden for maintenance
        public VisibilityStatus Status { get; set; } = VisibilityStatus.Public;

        // Private sections return 404 even when accessed directly by URL.
        public bool IsPrivate { get; set; } = false;

        public int SortOrder { get; set; } = 0;                   // Menu order

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
