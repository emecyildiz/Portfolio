using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Portfolio.Models;

namespace Portfolio.Services;

public interface ITicketEmailSender
{
    Task<TicketEmailSendResult> SendTicketReceivedAsync(
        TicketEmailOutbox outbox,
        CancellationToken cancellationToken);
}

public sealed record TicketEmailSendResult(
    bool Succeeded,
    bool Retryable,
    string? ProviderMessageId,
    string? ErrorCode)
{
    public static TicketEmailSendResult Success(string providerMessageId) =>
        new(true, false, providerMessageId, null);

    public static TicketEmailSendResult Failure(bool retryable, string errorCode) =>
        new(false, retryable, null, errorCode);
}

public sealed class ResendTicketEmailSender : ITicketEmailSender
{
    private static readonly Uri SendEmailEndpoint = new("https://api.resend.com/emails");

    private readonly HttpClient _httpClient;
    private readonly TicketEmailOptions _options;

    public ResendTicketEmailSender(
        HttpClient httpClient,
        IOptions<TicketEmailOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<TicketEmailSendResult> SendTicketReceivedAsync(
        TicketEmailOutbox outbox,
        CancellationToken cancellationToken)
    {
        var ticket = outbox.ContactMessage;
        var ticketNumber = ticket.TicketNumber.ToString("D");
        var publicBaseUrl = _options.PublicBaseUrl.TrimEnd('/');
        var trackingUrl = $"{publicBaseUrl}/hire#ticket={Uri.EscapeDataString(ticketNumber)}";

        var safeTicketNumber = HtmlEncoder.Default.Encode(ticketNumber);
        var safeTrackingUrl = HtmlEncoder.Default.Encode(trackingUrl);

        var textBody = $"""
            Your Emecworks request was received.

            Ticket number: {ticketNumber}

            Track the request:
            {trackingUrl}

            Keep the ticket number private. It is required to view the request status.
            This automated confirmation does not include the submitted message.
            """;

        var htmlBody = $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;background:#f4f5f7;color:#16181d;font-family:Arial,sans-serif;">
              <div style="max-width:620px;margin:0 auto;padding:40px 20px;">
                <div style="background:#ffffff;border:1px solid #dfe3e8;border-radius:14px;padding:32px;">
                  <p style="margin:0 0 10px;color:#5d6673;font-size:12px;letter-spacing:.12em;text-transform:uppercase;">Emecworks request channel</p>
                  <h1 style="margin:0 0 18px;font-size:26px;line-height:1.25;">Your request was received.</h1>
                  <p style="margin:0 0 22px;color:#4a5260;line-height:1.7;">Keep this ticket number private. It is required to view the request status.</p>
                  <div style="margin:0 0 24px;padding:16px;background:#f6f8fa;border:1px solid #e2e6ea;border-radius:9px;font-family:Consolas,monospace;font-size:15px;word-break:break-all;">{safeTicketNumber}</div>
                  <a href="{safeTrackingUrl}" style="display:inline-block;padding:12px 18px;background:#15181d;color:#ffffff;text-decoration:none;border-radius:7px;font-weight:600;">Track request</a>
                  <p style="margin:24px 0 0;color:#6a7380;font-size:13px;line-height:1.6;">This automated confirmation does not include the submitted message.</p>
                </div>
              </div>
            </body>
            </html>
            """;

        var payload = new Dictionary<string, object?>
        {
            ["from"] = $"{_options.FromName} <{_options.FromAddress}>",
            ["to"] = new[] { ticket.Email },
            ["subject"] = "Your Emecworks request was received",
            ["text"] = textBody,
            ["html"] = htmlBody
        };

        if (!string.IsNullOrWhiteSpace(_options.ReplyToAddress))
        {
            payload["reply_to"] = _options.ReplyToAddress;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, SendEmailEndpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key",
            $"ticket-received-{ticket.TicketNumber:N}");

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                await using var responseStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken);
                using var responseJson = await JsonDocument.ParseAsync(
                    responseStream,
                    cancellationToken: cancellationToken);
                var providerMessageId =
                    responseJson.RootElement.TryGetProperty("id", out var idElement)
                        ? idElement.GetString()
                        : null;

                return string.IsNullOrWhiteSpace(providerMessageId)
                    ? TicketEmailSendResult.Failure(true, "missing_provider_id")
                    : TicketEmailSendResult.Success(providerMessageId);
            }

            var retryable =
                response.StatusCode == HttpStatusCode.RequestTimeout ||
                response.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)response.StatusCode >= 500;

            return TicketEmailSendResult.Failure(
                retryable,
                $"http_{(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return TicketEmailSendResult.Failure(true, "request_timeout");
        }
        catch (HttpRequestException)
        {
            return TicketEmailSendResult.Failure(true, "network_error");
        }
        catch (JsonException)
        {
            return TicketEmailSendResult.Failure(true, "invalid_provider_response");
        }
    }
}
