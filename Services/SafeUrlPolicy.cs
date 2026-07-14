namespace Portfolio.Services;

public static class SafeUrlPolicy
{
    private static readonly HashSet<string> NavigationSchemes =
        [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto, "tel"];

    private static readonly HashSet<string> WebResourceSchemes =
        [Uri.UriSchemeHttp, Uri.UriSchemeHttps];

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

    public static bool IsSafeAbsoluteHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return IsAllowedAbsoluteUrl(value.Trim(), WebResourceSchemes);
    }

    private static bool IsSafeRelativeUrl(string value)
    {
        if (value.StartsWith("//") || value.Contains(':'))
            return false;

        return Uri.TryCreate(value, UriKind.Relative, out _);
    }

    private static bool IsAllowedAbsoluteUrl(string value, HashSet<string> allowedSchemes) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        allowedSchemes.Contains(uri.Scheme);
}
