using System.Text.RegularExpressions;

namespace Portfolio.Services;

public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    public bool Enabled { get; set; }
    public string SiteKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string ExpectedHostname { get; set; } = string.Empty;
    public string ExpectedAction { get; set; } = "contact";

    public void Validate(bool isProduction)
    {
        if (!Enabled)
            return;

        if (string.IsNullOrWhiteSpace(SiteKey))
            throw new InvalidOperationException("Turnstile:SiteKey is required when Turnstile is enabled.");

        if (string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("Turnstile:SecretKey is required when Turnstile is enabled.");

        if (isProduction && string.IsNullOrWhiteSpace(ExpectedHostname))
        {
            throw new InvalidOperationException(
                "Turnstile:ExpectedHostname is required when Turnstile is enabled in production.");
        }

        var hostNameType = Uri.CheckHostName(ExpectedHostname);
        if (!string.IsNullOrWhiteSpace(ExpectedHostname) &&
            hostNameType is not (
                UriHostNameType.Dns or
                UriHostNameType.IPv4 or
                UriHostNameType.IPv6))
        {
            throw new InvalidOperationException(
                "Turnstile:ExpectedHostname must be a valid host name or address without a scheme or path.");
        }

        if (!Regex.IsMatch(ExpectedAction, "^[A-Za-z0-9_-]{1,32}$"))
        {
            throw new InvalidOperationException(
                "Turnstile:ExpectedAction must contain 1-32 letters, numbers, hyphens, or underscores.");
        }
    }
}
