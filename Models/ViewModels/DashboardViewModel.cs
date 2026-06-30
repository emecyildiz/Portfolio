using Portfolio.Models;

namespace Portfolio.Models.ViewModels;

public class DashboardViewModel
{
    public int CategoryCount { get; set; }
    public int ProjectCount { get; set; }
    public int SecurityCount { get; set; }
    public int HomelabCount { get; set; }
    public int BlogCount { get; set; }
    public int TeamCount { get; set; }
    public int UnreadMessages { get; set; }
    public List<Category> RecentCategories { get; set; } = new();
}