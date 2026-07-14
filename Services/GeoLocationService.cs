using MaxMind.Db;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using System.Net;
using System.Net.Sockets;

namespace Portfolio.Services;

public interface IGeoLocationService
{
    Task<(string? Country, string? City)> LookupAsync(
        string ip,
        CancellationToken cancellationToken = default);
}

public sealed class GeoLocationService : IGeoLocationService, IDisposable
{
    private readonly ILogger<GeoLocationService> _logger;
    private readonly DatabaseReader? _reader;

    public GeoLocationService(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        ILogger<GeoLocationService> logger)
    {
        _logger = logger;

        var configuredPath = configuration["GeoLocation:DatabasePath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            _logger.LogWarning(
                "Local GeoIP lookup is disabled because GeoLocation:DatabasePath is not configured.");
            return;
        }

        var databasePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(hostEnvironment.ContentRootPath, configuredPath);
        databasePath = Path.GetFullPath(databasePath);

        if (!File.Exists(databasePath))
        {
            _logger.LogWarning(
                "Local GeoIP database was not found at {DatabasePath}. Location fields will remain empty.",
                databasePath);
            return;
        }

        try
        {
            _reader = new DatabaseReader(databasePath);
            _logger.LogInformation("Local GeoIP database loaded successfully.");
        }
        catch (InvalidDatabaseException exception)
        {
            _logger.LogError(
                exception,
                "The local GeoIP database is invalid. Location fields will remain empty.");
        }
        catch (IOException exception)
        {
            _logger.LogError(
                exception,
                "The local GeoIP database could not be opened. Location fields will remain empty.");
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Local GeoIP initialization failed. Location fields will remain empty.");
        }
    }

    public Task<(string? Country, string? City)> LookupAsync(
        string ip,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_reader is null ||
            !IPAddress.TryParse(ip, out var address) ||
            IsPrivateOrLocal(address))
        {
            return Task.FromResult<(string? Country, string? City)>((null, null));
        }

        try
        {
            var response = _reader.City(address);
            return Task.FromResult<(string? Country, string? City)>(
                (response.Country.Name, response.City.Name));
        }
        catch (AddressNotFoundException)
        {
            return Task.FromResult<(string? Country, string? City)>((null, null));
        }
        catch (InvalidDatabaseException exception)
        {
            _logger.LogWarning(
                exception,
                "The local GeoIP database could not complete a lookup.");
            return Task.FromResult<(string? Country, string? City)>((null, null));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Local GeoIP lookup failed. Location fields will remain empty.");
            return Task.FromResult<(string? Country, string? City)>((null, null));
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
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
