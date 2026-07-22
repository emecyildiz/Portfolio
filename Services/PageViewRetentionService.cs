using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

namespace Portfolio.Services;

public sealed class PageViewRetentionService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PageViewRetentionService> _logger;
    private readonly int _retentionDays;

    public PageViewRetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PageViewRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _retentionDays = configuration.GetValue("Privacy:PageViewRetentionDays", 30);

        if (_retentionDays is < 7 or > 365)
        {
            throw new InvalidOperationException(
                "Privacy:PageViewRetentionDays must be between 7 and 365 days.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DeleteExpiredAnalyticsRecordsAsync(stoppingToken);

        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await DeleteExpiredAnalyticsRecordsAsync(stoppingToken);
    }

    private async Task DeleteExpiredAnalyticsRecordsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deletedPageViewCount = await db.PageViews
                .Where(pageView => pageView.ViewedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            var deletedContentViewCount = await db.ContentViewReceipts
                .Where(receipt => receipt.CreatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedPageViewCount > 0 || deletedContentViewCount > 0)
            {
                _logger.LogInformation(
                    "Deleted {PageViewCount} visitor records and {ContentViewCount} content-view receipts older than {RetentionDays} days.",
                    deletedPageViewCount,
                    deletedContentViewCount,
                    _retentionDays);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Analytics retention cleanup failed.");
        }
    }
}
