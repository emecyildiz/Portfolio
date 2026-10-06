using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Portfolio.Services;

public static class AdminMfaRateLimit
{
    public const string PolicyName = "AdminMfaLimit";

    public static void Configure(RateLimiterOptions options)
    {
        // Keep fresh-password attempts bounded without consuming login permits.
        options.AddPolicy(PolicyName, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(15),
                    PermitLimit = 5,
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    }
}
