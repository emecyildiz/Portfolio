using Portfolio.Services;

namespace Portfolio.Middleware;

/// <summary>Must run after authentication and before every static-file server.</summary>
public sealed class UploadAccessMiddleware(RequestDelegate next, IWebHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext context, UploadAccessPolicy policy)
    {
        if (!UploadPath.IsUploadRequest(context.Request.Path))
        {
            await next(context);
            return;
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "private, no-store, no-cache, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Task.CompletedTask;
        });

        var path = context.Request.Path.Value ?? "";
        if ((!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)) ||
            !UploadPath.IsCanonicalPath(path) || !IsRegularUploadFile(path) ||
            (context.User.Identity?.IsAuthenticated != true &&
             !await policy.CanReadAsync(path, context.RequestAborted)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next(context);
    }

    private bool IsRegularUploadFile(string path)
    {
        try { return IsRegularUploadFileCore(path); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private bool IsRegularUploadFileCore(string path)
    {
        var root = Path.GetFullPath(Path.Combine(env.WebRootPath, "uploads"));
        var file = Path.GetFullPath(Path.Combine(env.WebRootPath, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!file.StartsWith(root + Path.DirectorySeparatorChar, comparison) || !File.Exists(file)) return false;
        // Do not let a symlink/junction reinterpret an approved URL as another file.
        for (var current = file; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
            if (string.Equals(current, root, comparison)) return true;
        }
        return false;
    }
}
