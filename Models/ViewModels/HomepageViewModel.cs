using Portfolio.Models;

namespace Portfolio.Models.ViewModels;

public class HomepageViewModel
{
    public List<Project> FeaturedProjects { get; set; } = new();
    public List<SecurityResearch> FeaturedSecurity { get; set; } = new();
    public List<HomelabPost> FeaturedHomelab { get; set; } = new();
    public List<BlogPost> RecentBlog { get; set; } = new();
}