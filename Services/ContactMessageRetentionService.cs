using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

namespace Portfolio.Services;

public sealed class ContactMessageRetentionService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ContactMessageRetentionService> _logger;
    private readonly int _securityMetadataDays;
    private readonly int _messageRetentionDays;

    public ContactMessageRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ContactMessageRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _securityMetadataDays = configuration.GetValue(
            "Privacy:TicketSecurityMetadataDays",
            30);
        _messageRetentionDays = configuration.GetValue(
            "Privacy:TicketRetentionDays",
            365);

        if (_securityMetadataDays is < 7 or > 90)
        {
            throw new InvalidOperationException(
                "Privacy:TicketSecurityMetadataDays must be between 7 and 90 days.");
        }

        if (_messageRetentionDays is < 30 or > 1095 ||
            _messageRetentionDays <= _securityMetadataDays)
        {
            throw new InvalidOperationException(
                "Privacy:TicketRetentionDays must be 30-1095 days and longer than the security-metadata period.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ApplyRetentionAsync(stoppingToken);

        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ApplyRetentionAsync(stoppingToken);
    }

    private async Task ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var securityCutoff = now.AddDays(-_securityMetadataDays);
            var messageCutoff = now.AddDays(-_messageRetentionDays);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var clearedSecurityMetadata = await db.ContactMessages
                .Where(message =>
                    message.CreatedAt < securityCutoff &&
                    (message.IpAddress != null || message.UserAgent != null))
                .ExecuteUpdateAsync(
                    updates => updates
                        .SetProperty(message => message.IpAddress, (string?)null)
                        .SetProperty(message => message.UserAgent, (string?)null),
                    cancellationToken);

            var deletedMessages = await db.ContactMessages
                .Where(message => message.CreatedAt < messageCutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (clearedSecurityMetadata > 0 || deletedMessages > 0)
            {
                _logger.LogInformation(
                    "Cleared security metadata from {MetadataCount} tickets and deleted {MessageCount} expired tickets.",
                    clearedSecurityMetadata,
                    deletedMessages);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Ticket retention cleanup failed.");
        }
    }
}
