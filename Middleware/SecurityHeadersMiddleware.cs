namespace Portfolio.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private const string ContentSecurityPolicy =
        "frame-ancestors 'none'; object-src 'none'; base-uri 'none'";

    private readonly RequestDelegate _next;
    private readonly PathString _adminPrefix;

    public SecurityHeadersMiddleware(RequestDelegate next, string adminPath)
    {
        _next = next;
        _adminPrefix = new PathString($"/{adminPath}");
    }

    public Task InvokeAsync(HttpContext context)
    {
        var isAdminRequest = context.Request.Path.StartsWithSegments(
            _adminPrefix,
            StringComparison.OrdinalIgnoreCase);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers.ContentSecurityPolicy = ContentSecurityPolicy;
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
            }

            return Task.CompletedTask;
        });

        return _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UsePortfolioSecurityHeaders(
        this IApplicationBuilder app,
        string adminPath)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>(adminPath);
    }
}
