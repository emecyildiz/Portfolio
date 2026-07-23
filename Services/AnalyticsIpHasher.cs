using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Portfolio.Services;

public interface IAnalyticsIpHasher
{
    string CreateDailyHash(IPAddress address, DateOnly date);
}

public sealed class AnalyticsIpHasher : IAnalyticsIpHasher
{
    private const int RequiredKeySizeBytes = 32;
    private readonly byte[] _key;

    public AnalyticsIpHasher(IConfiguration configuration)
    {
        var encodedKey = configuration["Privacy:AnalyticsHashKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            throw new InvalidOperationException(
                "Privacy:AnalyticsHashKey must be configured with a Base64-encoded 32-byte secret.");
        }

        try
        {
            _key = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Privacy:AnalyticsHashKey must be valid Base64.",
                exception);
        }

        if (_key.Length != RequiredKeySizeBytes)
        {
            throw new InvalidOperationException(
                "Privacy:AnalyticsHashKey must decode to exactly 32 bytes.");
        }
    }

    public string CreateDailyHash(IPAddress address, DateOnly date)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var input = Encoding.UTF8.GetBytes($"{date:yyyy-MM-dd}|{address}");
        var hash = HMACSHA256.HashData(_key, input);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
