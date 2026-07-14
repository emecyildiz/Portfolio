namespace Portfolio.Models.ViewModels;

public class ActivityItem
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;        // "security", "electronics", "homelab", "blog", "team"
    public string TypeLabel { get; set; } = string.Empty;   // Visible label
    public string ColorClass { get; set; } = string.Empty;  // Tailwind color class
    public DateTime? Date { get; set; }
}
