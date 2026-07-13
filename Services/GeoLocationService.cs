using System.Text.Json;

namespace Portfolio.Services;

public interface IGeoLocationService
{
    Task<(string? Country, string? City)> LookupAsync(string ip);
}

public class GeoLocationService : IGeoLocationService
{
    private readonly HttpClient _http;

    public GeoLocationService(HttpClient http)
    {
        _http = http;
    }

    public async Task<(string? Country, string? City)> LookupAsync(string ip)
    {
        // Lokal/özel IP'ler için sorgu atma (localhost, Docker iç network)
        if (ip == "unknown" || ip.StartsWith("127.") || ip.StartsWith("172.") || ip.StartsWith("192.168."))
            return (null, null);

        try
        {
            // ip-api.com ücretsiz, HTTPS desteklemiyor (free tier), dikkat
            var response = await _http.GetStringAsync($"http://ip-api.com/json/{ip}?fields=status,country,city");
            var json = JsonDocument.Parse(response);

            if (json.RootElement.GetProperty("status").GetString() == "success")
            {
                var country = json.RootElement.TryGetProperty("country", out var c) ? c.GetString() : null;
                var city = json.RootElement.TryGetProperty("city", out var ci) ? ci.GetString() : null;
                return (country, city);
            }
        }
        catch
        {
            // Servis erişilemezse sessizce boş dön, siteyi bozmasın
        }

        return (null, null);
    }
}