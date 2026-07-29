namespace Portfolio.Models;

public sealed class TicketEmailOutbox
{
    public long Id { get; set; }

    public int ContactMessageId { get; set; }
    public ContactMessage ContactMessage { get; set; } = null!;

    public string Kind { get; set; } = TicketEmailKinds.TicketReceived;
    public string? Body { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? FailedAt { get; set; }

    public string? ProviderMessageId { get; set; }
    public string? LastErrorCode { get; set; }
}

public static class TicketEmailKinds
{
    public const string TicketReceived = "TicketReceived";
    public const string TicketReply = "TicketReply";
}
