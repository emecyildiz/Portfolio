using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Portfolio.Data;
using Portfolio.Models;

namespace Portfolio.Services;

public sealed class TicketEmailDeliveryService : BackgroundService
{
    private const int BatchSize = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITicketEmailSender _sender;
    private readonly TicketEmailOptions _options;
    private readonly ILogger<TicketEmailDeliveryService> _logger;

    public TicketEmailDeliveryService(
        IServiceScopeFactory scopeFactory,
        ITicketEmailSender sender,
        IOptions<TicketEmailOptions> options,
        ILogger<TicketEmailDeliveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Ticket email delivery is disabled; queued messages will remain pending.");
            return;
        }

        await ProcessBatchAsync(stoppingToken);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_options.PollIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessBatchAsync(stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var pending = await db.TicketEmailOutboxes
                .Include(outbox => outbox.ContactMessage)
                .Where(outbox =>
                    outbox.SentAt == null &&
                    outbox.FailedAt == null &&
                    outbox.NextAttemptAt <= now)
                .OrderBy(outbox => outbox.NextAttemptAt)
                .ThenBy(outbox => outbox.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0)
            {
                return;
            }

            var sentToday = await db.TicketEmailOutboxes.CountAsync(
                outbox => outbox.SentAt >= now.Date,
                cancellationToken);
            var remainingDailyCapacity = _options.DailySendLimit - sentToday;

            foreach (var outbox in pending.Take(Math.Max(0, remainingDailyCapacity)))
            {
                try
                {
                    await DeliverAsync(db, outbox, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    outbox.AttemptCount++;
                    outbox.LastErrorCode = "unexpected_sender_error";

                    if (outbox.AttemptCount >= _options.MaxAttempts)
                    {
                        outbox.FailedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        outbox.NextAttemptAt = DateTime.UtcNow.Add(
                            GetRetryDelay(outbox.AttemptCount));
                    }

                    await db.SaveChangesAsync(cancellationToken);

                    _logger.LogError(
                        "Ticket email outbox item {OutboxId} encountered an unexpected sender error.",
                        outbox.Id);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Ticket email outbox processing failed.");
        }
    }

    private async Task DeliverAsync(
        AppDbContext db,
        TicketEmailOutbox outbox,
        CancellationToken cancellationToken)
    {
        if (outbox.Kind == TicketEmailKinds.TicketReply &&
            outbox.ContactMessage.Status ==
                Portfolio.Models.Enums.ContactStatus.Spam)
        {
            outbox.FailedAt = DateTime.UtcNow;
            outbox.LastErrorCode = "reply_blocked_ticket_status";
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Ticket reply outbox item {OutboxId} was blocked because the request is marked as spam.",
                outbox.Id);
            return;
        }

        outbox.AttemptCount++;

        var result = await _sender.SendAsync(
            outbox,
            cancellationToken);

        if (result.Succeeded)
        {
            outbox.SentAt = DateTime.UtcNow;
            outbox.ProviderMessageId = result.ProviderMessageId;
            outbox.LastErrorCode = null;

            if (outbox.Kind == TicketEmailKinds.TicketReply)
            {
                outbox.ContactMessage.Status =
                    Portfolio.Models.Enums.ContactStatus.Replied;
                outbox.ContactMessage.IsRead = true;
            }

            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Ticket email outbox item {OutboxId} was delivered.",
                outbox.Id);
            return;
        }

        outbox.LastErrorCode = result.ErrorCode;

        if (!result.Retryable || outbox.AttemptCount >= _options.MaxAttempts)
        {
            outbox.FailedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogError(
                "Ticket email outbox item {OutboxId} failed permanently with code {ErrorCode}.",
                outbox.Id,
                result.ErrorCode);
            return;
        }

        outbox.NextAttemptAt = DateTime.UtcNow.Add(
            GetRetryDelay(outbox.AttemptCount));
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Ticket email outbox item {OutboxId} will be retried after error code {ErrorCode}.",
            outbox.Id,
            result.ErrorCode);
    }

    private static TimeSpan GetRetryDelay(int attemptCount) =>
        attemptCount switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            4 => TimeSpan.FromHours(1),
            5 => TimeSpan.FromHours(6),
            _ => TimeSpan.FromHours(24)
        };
}
