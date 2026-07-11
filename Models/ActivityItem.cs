namespace Portfolio.Models.ViewModels;

public class ActivityItem
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;        // "security", "electronics", "homelab", "blog", "team"
    public string TypeLabel { get; set; } = string.Empty;   // Görünen etiket
    public string ColorClass { get; set; } = string.Empty;  // Tailwind renk sınıfı
    public DateTime? Date { get; set; }
}