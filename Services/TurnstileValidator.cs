using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Portfolio.Services;

public interface ITurnstileValidator
{
    bool IsEnabled { get; }

    Task<bool> ValidateAsync(
        string? token,
        string? remoteIpAddress,
        CancellationToken cancellationToken);
}

public sealed class TurnstileValidator : ITurnstileValidator
{
    private const string SiteverifyPath = "turnstile/v0/siteverify";

    private readonly HttpClient _httpClient;
    private readonly ILogger<TurnstileValidator> _logger;
    private readonly TurnstileOptions _options;

    public TurnstileValidator(
        HttpClient httpClient,
        IOptions<TurnstileOptions> options,
        ILogger<TurnstileValidator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public bool IsEnabled => _options.Enabled;

    public async Task<bool> ValidateAsync(
        string? token,
        string? remoteIpAddress,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return true;

        token = token?.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            return false;

        var request = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey,
            ["response"] = token
        };

        if (!string.IsNullOrWhiteSpace(remoteIpAddress))
            request["remoteip"] = remoteIpAddress;

        try
        {
            using var content = new FormUrlEncodedContent(request);
            using var response = await _httpClient.PostAsync(
                SiteverifyPath,
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Turnstile Siteverify returned HTTP {StatusCode}.",
                    (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<TurnstileSiteverifyResponse>(
                cancellationToken: cancellationToken);

            if (result?.Success != true)
            {
                _logger.LogInformation(
                    "Turnstile validation rejected a contact submission. Codes: {ErrorCodes}",
                    result?.ErrorCodes is { Length: > 0 }
                        ? string.Join(",", result.ErrorCodes)
                        : "none");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_options.ExpectedHostname) &&
                !string.Equals(
                    result.Hostname,
                    _options.ExpectedHostname,
                    StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Turnstile validation returned an unexpected hostname.");
                return false;
            }

            if (!string.Equals(
                    result.Action,
                    _options.ExpectedAction,
                    StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Turnstile validation returned an unexpected action.");
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Turnstile Siteverify timed out.");
            return false;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Turnstile Siteverify could not be reached.");
            return false;
        }
        catch (NotSupportedException exception)
        {
            _logger.LogWarning(
                exception,
                "Turnstile Siteverify returned an unsupported response.");
            return false;
        }
        catch (System.Text.Json.JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Turnstile Siteverify returned invalid JSON.");
            return false;
        }
    }

    private sealed class TurnstileSiteverifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("hostname")]
        public string? Hostname { get; init; }

        [JsonPropertyName("action")]
        public string? Action { get; init; }

        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; init; }
    }
}
