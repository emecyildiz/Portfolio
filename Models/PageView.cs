namespace Portfolio.Models;

public class PageView
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string VisitorHash { get; set; } = string.Empty;
    public DateOnly ViewDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
    public string? Country { get; set; }
}
