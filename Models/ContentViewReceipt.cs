namespace Portfolio.Models;

public sealed class ContentViewReceipt
{
    public long Id { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public int ContentId { get; set; }
    public string VisitorHash { get; set; } = string.Empty;
    public DateOnly ViewDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
