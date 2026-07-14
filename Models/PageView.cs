namespace Portfolio.Models;

public class PageView
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
    public string? Country { get; set; }
    public string? City { get; set; }
}
