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
        await DeleteExpiredPageViewsAsync(stoppingToken);

        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await DeleteExpiredPageViewsAsync(stoppingToken);
    }

    private async Task DeleteExpiredPageViewsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deletedCount = await db.PageViews
                .Where(pageView => pageView.ViewedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "Deleted {Count} page-view records older than {RetentionDays} days.",
                    deletedCount,
                    _retentionDays);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Page-view retention cleanup failed.");
        }
    }
}
