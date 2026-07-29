namespace Portfolio.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PathString _adminPrefix;
    private readonly string _contentSecurityPolicy;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        string adminPath,
        bool turnstileEnabled)
    {
        _next = next;
        _adminPrefix = new PathString($"/{adminPath}");
        _contentSecurityPolicy = BuildContentSecurityPolicy(turnstileEnabled);
    }

    public Task InvokeAsync(HttpContext context)
    {
        var isAdminRequest = context.Request.Path.StartsWithSegments(
            _adminPrefix,
            StringComparison.OrdinalIgnoreCase);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers.ContentSecurityPolicy = _contentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["X-XSS-Protection"] = "0";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
            headers["X-Permitted-Cross-Domain-Policies"] = "none";

            if (isAdminRequest)
            {
                headers.CacheControl = "no-store, no-cache, must-revalidate";
                headers.Pragma = "no-cache";
                headers.Expires = "0";
                headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }

    private static string BuildContentSecurityPolicy(bool turnstileEnabled)
    {
        var turnstileOrigin = turnstileEnabled
            ? " https://challenges.cloudflare.com"
            : string.Empty;

        return
            "default-src 'self'; " +
            "base-uri 'none'; " +
            $"connect-src 'self'{turnstileOrigin}; " +
            "font-src 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            $"frame-src {(turnstileEnabled ? "https://challenges.cloudflare.com" : "'none'")}; " +
            "img-src 'self' https: data:; " +
            "object-src 'none'; " +
            $"script-src 'self' 'unsafe-inline'{turnstileOrigin}; " +
            "style-src 'self' 'unsafe-inline'";
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UsePortfolioSecurityHeaders(
        this IApplicationBuilder app,
        string adminPath,
        bool turnstileEnabled)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>(
            adminPath,
            turnstileEnabled);
    }
}
