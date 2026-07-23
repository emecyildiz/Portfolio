namespace Portfolio.Services;

public static class AnalyticsConsent
{
    public const string CookieName = "portfolio_analytics_consent";
    public const string VisitorCookieName = "portfolio_visitor";
    public const string GrantedValue = "accepted";
    public const string DeniedValue = "rejected";

    public static bool IsGranted(HttpContext context) =>
        string.Equals(
            GetDecision(context),
            GrantedValue,
            StringComparison.Ordinal);

    public static string? GetDecision(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var decision))
            return null;

        return decision is GrantedValue or DeniedValue ? decision : null;
    }
}
