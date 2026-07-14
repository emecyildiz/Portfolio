using System.Text.Json;

using System.Net;
using System.Net.Sockets;

namespace Portfolio.Services;

public interface IGeoLocationService
{
    Task<(string? Country, string? City)> LookupAsync(
        string ip,
        CancellationToken cancellationToken = default);
}

public class GeoLocationService : IGeoLocationService
{
    private readonly HttpClient _http;

    public GeoLocationService(HttpClient http)
    {
        _http = http;
    }

    public async Task<(string? Country, string? City)> LookupAsync(
        string ip,
        CancellationToken cancellationToken = default)
    {
        // Never send local or private addresses to an external provider.
        if (!IPAddress.TryParse(ip, out var address) || IsPrivateOrLocal(address))
        {
            return (null, null);
        }

        try
        {
            // Replace this HTTP-only free provider before production deployment.
            using var response = await _http.GetAsync(
                $"http://ip-api.com/json/{Uri.EscapeDataString(ip)}?fields=status,country,city",
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(
                responseStream,
                cancellationToken: cancellationToken);

            if (json.RootElement.GetProperty("status").GetString() == "success")
            {
                var country = json.RootElement.TryGetProperty("country", out var c) ? c.GetString() : null;
                var city = json.RootElement.TryGetProperty("city", out var ci) ? ci.GetString() : null;
                return (country, city);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // A geolocation failure must never break the public page.
        }

        return (null, null);
    }

    private static bool IsPrivateOrLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 0 ||
                   bytes[0] == 10 ||
                   (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
               (address.IsIPv6LinkLocal ||
                address.IsIPv6SiteLocal ||
                (bytes[0] & 0xFE) == 0xFC);
    }
}
