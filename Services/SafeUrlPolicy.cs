namespace Portfolio.Services;

public static class SafeUrlPolicy
{
    private static readonly HashSet<string> NavigationSchemes =
        [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto, "tel"];

    private static readonly HashSet<string> WebResourceSchemes =
        [Uri.UriSchemeHttps];

    public static bool IsSafeNavigationUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var url = value.Trim();
        return IsSafeRelativeUrl(url) ||
               IsAllowedAbsoluteUrl(url, NavigationSchemes);
    }

    public static bool IsSafeWebResourceUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var url = value.Trim();
        return IsSafeRelativeUrl(url) ||
               IsAllowedAbsoluteUrl(url, WebResourceSchemes);
    }

    public static bool IsSafeAbsoluteHttpsUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               !string.IsNullOrWhiteSpace(uri.Host);
    }

    private static bool IsSafeRelativeUrl(string value)
    {
        if (value.StartsWith("//") ||
            value.Contains(':') ||
            value.Contains('\\') ||
            value.Any(char.IsControl))
            return false;

        return Uri.TryCreate(value, UriKind.Relative, out _);
    }

    private static bool IsAllowedAbsoluteUrl(string value, HashSet<string> allowedSchemes)
    {
        if (value.Any(char.IsControl) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !allowedSchemes.Contains(uri.Scheme))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            return !string.IsNullOrWhiteSpace(uri.Host);

        return !string.IsNullOrWhiteSpace(uri.AbsolutePath);
    }
}
