using System.Net.Mail;

namespace Portfolio.Services;

public sealed class TicketEmailOptions
{
    public const string SectionName = "TicketEmail";

    public bool Enabled { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string FromName { get; set; } = "Emecworks";
    public string FromAddress { get; set; } = string.Empty;
    public string? ReplyToAddress { get; set; }
    public string PublicBaseUrl { get; set; } = "https://emecworks.com";
    public int PollIntervalSeconds { get; set; } = 30;
    public int MaxAttempts { get; set; } = 8;
    public int DailySendLimit { get; set; } = 80;

    public void Validate()
    {
        if (PollIntervalSeconds is < 10 or > 300)
        {
            throw new InvalidOperationException(
                "TicketEmail:PollIntervalSeconds must be between 10 and 300.");
        }

        if (MaxAttempts is < 3 or > 12)
        {
            throw new InvalidOperationException(
                "TicketEmail:MaxAttempts must be between 3 and 12.");
        }

        if (DailySendLimit is < 10 or > 500)
        {
            throw new InvalidOperationException(
                "TicketEmail:DailySendLimit must be between 10 and 500.");
        }

        if (!Enabled)
        {
            return;
        }

        if (ApiKey.Length is < 20 or > 256 ||
            !ApiKey.StartsWith("re_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "TicketEmail:ApiKey must contain a valid Resend API key.");
        }

        if (string.IsNullOrWhiteSpace(FromName) ||
            FromName.Length > 100 ||
            FromName.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "TicketEmail:FromName must contain 1-100 printable characters.");
        }

        if (!MailAddress.TryCreate(FromAddress, out var fromAddress) ||
            !string.Equals(
                fromAddress.Address,
                FromAddress.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "TicketEmail:FromAddress must contain one bare email address.");
        }

        if (!string.IsNullOrWhiteSpace(ReplyToAddress) &&
            (!MailAddress.TryCreate(ReplyToAddress, out var replyToAddress) ||
             !string.Equals(
                 replyToAddress.Address,
                 ReplyToAddress.Trim(),
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "TicketEmail:ReplyToAddress must contain one bare email address.");
        }

        if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var publicBaseUri) ||
            publicBaseUri.Scheme != Uri.UriSchemeHttps ||
            publicBaseUri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(publicBaseUri.Query) ||
            !string.IsNullOrEmpty(publicBaseUri.Fragment))
        {
            throw new InvalidOperationException(
                "TicketEmail:PublicBaseUrl must be an HTTPS origin without a path, query, or fragment.");
        }
    }
}
