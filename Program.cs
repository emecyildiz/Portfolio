using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Middleware;
using Portfolio.Services;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);
var configuredAdminPath = builder.Configuration["AdminPath"]?.Trim().Trim('/');
var weeklyReportToken = builder.Configuration["Monitoring:WeeklyReportToken"]?.Trim();
var ticketEmailOptions = new TicketEmailOptions();
builder.Configuration
    .GetSection(TicketEmailOptions.SectionName)
    .Bind(ticketEmailOptions);
ticketEmailOptions.Validate();
var adminPath = string.IsNullOrWhiteSpace(configuredAdminPath)
    ? builder.Environment.IsDevelopment()
        ? "panel"
        : throw new InvalidOperationException("AdminPath must be configured in production.")
    : configuredAdminPath;

if (!Regex.IsMatch(adminPath, "^[A-Za-z0-9][A-Za-z0-9_-]{2,63}$"))
{
    throw new InvalidOperationException(
        "AdminPath must be 3-64 characters and contain only letters, numbers, hyphens, or underscores.");
}

if (!string.IsNullOrWhiteSpace(weeklyReportToken) &&
    !Regex.IsMatch(weeklyReportToken, "^[A-Fa-f0-9]{64,128}$"))
{
    throw new InvalidOperationException(
        "Monitoring:WeeklyReportToken must contain 64 to 128 hexadecimal characters.");
}

if (builder.Environment.IsProduction() &&
    string.IsNullOrWhiteSpace(weeklyReportToken))
{
    throw new InvalidOperationException(
        "Monitoring:WeeklyReportToken must be configured in production.");
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection must be configured.");

if (builder.Environment.IsProduction() &&
    connectionString.Contains("Password=change-me", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "The placeholder database password cannot be used in production.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql =>
        {
            npgsql.MigrationsAssembly("Portfolio");
            npgsql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null
            );
        }
    )
    
);

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    // Password requirements
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;

    // Lock the account for 15 minutes after 5 failed attempts.
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// Authentication cookie settings
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = $"/{adminPath}/account/login";
    options.LogoutPath = $"/{adminPath}/account/logout";
    options.AccessDeniedPath = $"/{adminPath}/account/login";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("ContactFormLimit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromHours(1),
                PermitLimit = 3,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("AdminLoginLimit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(15),
                PermitLimit = 5,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("TicketTrackingLimit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 10,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("SearchLimit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 30,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture);
        }

        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please wait before trying again.", cancellationToken);
    };
});

if (builder.Environment.IsProduction())
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;

        // Production Compose exposes Kestrel only on 127.0.0.1, so Nginx is the
        // sole source allowed to supply forwarded headers.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddScoped<ISlugService, SlugService>();
builder.Services.AddScoped<IReadingTimeService, ReadingTimeService>();
builder.Services.AddScoped<IViewCountService, ViewCountService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddSingleton<IGeoLocationService, GeoLocationService>();
builder.Services.AddSingleton<IAnalyticsIpHasher, AnalyticsIpHasher>();
builder.Services.AddSingleton(
    Microsoft.Extensions.Options.Options.Create(ticketEmailOptions));
builder.Services.AddHttpClient<ITicketEmailSender, ResendTicketEmailSender>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHostedService<PageViewRetentionService>();
builder.Services.AddHostedService<ContactMessageRetentionService>();
builder.Services.AddHostedService<TicketEmailDeliveryService>();
builder.Services.AddMemoryCache();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/dataprotection-keys"));


var app = builder.Build();

if (app.Environment.IsProduction())
{
    app.UseForwardedHeaders();
}

app.UsePortfolioSecurityHeaders(adminPath);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    _ = scope.ServiceProvider.GetRequiredService<IGeoLocationService>();
    _ = scope.ServiceProvider.GetRequiredService<IAnalyticsIpHasher>();
    db.Database.Migrate();

    // Seed the initial admin only when explicit credentials are configured.
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Startup");

    var adminEmail = builder.Configuration["AdminEmail"]?.Trim();
    var adminPassword = builder.Configuration["AdminPassword"];

    if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
    {
        if (app.Environment.IsProduction())
        {
            throw new InvalidOperationException(
                "AdminEmail and AdminPassword must be configured in production.");
        }

        startupLogger.LogWarning(
            "Admin seed was skipped because AdminEmail or AdminPassword is not configured.");
    }
    else if (await userManager.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new IdentityUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(admin, adminPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(
                "; ",
                result.Errors.Select(error => $"{error.Code}: {error.Description}"));
            throw new InvalidOperationException($"Admin account could not be created. {errors}");
        }

        startupLogger.LogInformation("Initial admin account was created.");
    }

    // Seed categories only for a new database.
    if (!await db.Categories.AnyAsync())
    {
        var categories = new List<Portfolio.Models.Category>
        {
            new() { Name = "Security", Slug = "security", IconClass = "ti ti-shield", SortOrder = 1, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
            new() { Name = "Electronics", Slug = "electronics", IconClass = "ti ti-cpu", SortOrder = 2, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
            new() { Name = "Web Applications", Slug = "webapps", IconClass = "ti ti-browser", SortOrder = 3, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
            new() { Name = "Homelab", Slug = "homelab", IconClass = "ti ti-server", SortOrder = 4, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
            new() { Name = "Blog", Slug = "blog", IconClass = "ti ti-pencil", SortOrder = 5, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
            new() { Name = "Team & Hackathon", Slug = "team", IconClass = "ti ti-users", SortOrder = 6, Status = Portfolio.Models.Enums.VisibilityStatus.Public },
            new() { Name = "Notes", Slug = "notes", IconClass = "ti ti-notes", SortOrder = 7, Status = Portfolio.Models.Enums.VisibilityStatus.Private, IsPrivate = true },
        };

        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error/{0}");
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

var pageViewLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("PageViewTracking");
var pageViewSkipPrefixes = new[]
{
    new PathString($"/{adminPath}"),
    new PathString("/css"),
    new PathString("/js"),
    new PathString("/lib"),
    new PathString("/uploads"),
    new PathString("/icons"),
    new PathString("/.well-known"),
    new PathString("/health"),
    new PathString("/blog/rss.xml"),
    new PathString("/favicon.ico"),
    new PathString("/robots.txt"),
    new PathString("/sitemap.xml"),
    new PathString("/error")
};

app.Use(async (context, next) =>
{
    await next();

    var shouldSkip = pageViewSkipPrefixes.Any(prefix =>
        context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    if (!HttpMethods.IsGet(context.Request.Method) ||
        shouldSkip ||
        !AnalyticsConsent.IsGranted(context) ||
        context.Response.StatusCode >= StatusCodes.Status400BadRequest)
    {
        return;
    }

    try
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return;
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        var ip = remoteIp.ToString();
        var viewedAt = DateTime.UtcNow;
        var viewDate = DateOnly.FromDateTime(viewedAt);

        using var scope = context.RequestServices.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ipHasher = scope.ServiceProvider.GetRequiredService<IAnalyticsIpHasher>();
        var visitorHash = ipHasher.CreateDailyHash(remoteIp, viewDate);

        var alreadyVisitedToday = await db.PageViews
            .AnyAsync(
                pageView => pageView.VisitorHash == visitorHash &&
                            pageView.ViewDate == viewDate,
                context.RequestAborted);

        if (!alreadyVisitedToday)
        {
            var geoService = scope.ServiceProvider.GetRequiredService<IGeoLocationService>();
            var country = await geoService.LookupCountryAsync(ip, context.RequestAborted);

            db.PageViews.Add(new Portfolio.Models.PageView
            {
                Path = context.Request.Path.Value ?? "/",
                VisitorHash = visitorHash,
                ViewDate = viewDate,
                Country = country,
                ViewedAt = viewedAt
            });
            await db.SaveChangesAsync(context.RequestAborted);
        }
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // The client disconnected; no page view needs to be recorded.
    }
    catch (Exception exception)
    {
        pageViewLogger.LogWarning(
            exception,
            "Page view tracking failed for {Path}.",
            context.Request.Path);
    }
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.MapGet("/health/live", (HttpContext context) =>
{
    SetHealthResponseHeaders(context);
    return Results.Text("Healthy", "text/plain", System.Text.Encoding.UTF8);
}).ExcludeFromDescription();

app.MapGet("/health/ready", async (
    HttpContext context,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    SetHealthResponseHeaders(context);

    try
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);
        return Results.Text(
            canConnect ? "Healthy" : "Unhealthy",
            "text/plain",
            System.Text.Encoding.UTF8,
            canConnect
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch
    {
        return Results.Text(
            "Unhealthy",
            "text/plain",
            System.Text.Encoding.UTF8,
            StatusCodes.Status503ServiceUnavailable);
    }
}).ExcludeFromDescription();

app.MapGet("/internal/analytics/weekly", async (
    HttpContext context,
    AppDbContext db,
    CancellationToken cancellationToken) =>
{
    SetPrivateResponseHeaders(context);

    if (!HasValidInternalToken(context, weeklyReportToken))
    {
        return Results.NotFound();
    }

    var periodEnd = DateOnly.FromDateTime(DateTime.UtcNow);
    var periodStart = periodEnd.AddDays(-7);
    var rows = await db.PageViews
        .AsNoTracking()
        .Where(pageView =>
            pageView.ViewDate >= periodStart &&
            pageView.ViewDate < periodEnd)
        .Select(pageView => new
        {
            pageView.ViewDate,
            pageView.Country,
            pageView.Path
        })
        .ToListAsync(cancellationToken);

    var daily = Enumerable.Range(0, 7)
        .Select(offset =>
        {
            var date = periodStart.AddDays(offset);
            return new
            {
                Date = date,
                Count = rows.Count(row => row.ViewDate == date)
            };
        })
        .ToArray();

    var countries = rows
        .Where(row => !string.IsNullOrWhiteSpace(row.Country))
        .GroupBy(
            row => row.Country!.Trim(),
            StringComparer.OrdinalIgnoreCase)
        .Select(group => new
        {
            Country = group.Key,
            Count = group.Count()
        })
        .OrderByDescending(item => item.Count)
        .ThenBy(item => item.Country)
        .ToArray();
    var reportedCountries = countries
        .Where(item => item.Count >= 2)
        .Take(5)
        .ToArray();

    var entryPaths = rows
        .GroupBy(row => string.IsNullOrWhiteSpace(row.Path) ? "/" : row.Path)
        .Select(group => new
        {
            Path = group.Key,
            Count = group.Count()
        })
        .OrderByDescending(item => item.Count)
        .ThenBy(item => item.Path)
        .ToArray();
    var reportedEntryPaths = entryPaths
        .Where(item => item.Count >= 2)
        .Take(5)
        .ToArray();

    return Results.Json(new
    {
        PeriodStart = periodStart,
        PeriodEndExclusive = periodEnd,
        TotalDailyUniqueVisits = rows.Count,
        AverageDailyUniqueVisits = Math.Round(rows.Count / 7d, 1),
        Daily = daily,
        Countries = reportedCountries,
        OtherOrLowVolumeCountryVisits =
            rows.Count - reportedCountries.Sum(item => item.Count),
        EntryPaths = reportedEntryPaths,
        OtherOrLowVolumeEntryPathVisits =
            rows.Count - reportedEntryPaths.Sum(item => item.Count)
    });
}).ExcludeFromDescription();

app.MapGet("/robots.txt", (HttpContext context) =>
{
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}{context.Request.PathBase}";
    var content = $"""
        User-agent: *
        Allow: /

        Sitemap: {baseUrl}/sitemap.xml
        """;

    return Results.Text(content, "text/plain", System.Text.Encoding.UTF8);
}).ExcludeFromDescription();


app.MapControllerRoute(
    name: "admin",
    pattern: $"{adminPath}/{{controller=Dashboard}}/{{action=Index}}/{{id?}}",
    defaults: new { area = "Admin" }
);


app.MapControllerRoute(
    name: "security_detail",
    pattern: "security/{slug}",
    defaults: new { controller = "Security", action = "Detail" }
);

app.MapControllerRoute(
    name: "security",
    pattern: "security",
    defaults: new { controller = "Security", action = "Index" }
);
app.MapControllerRoute(
    name: "electronics_detail",
    pattern: "electronics/{slug}",
    defaults: new { controller = "Electronics", action = "Detail" }
);

app.MapControllerRoute(
    name: "electronics",
    pattern: "electronics",
    defaults: new { controller = "Electronics", action = "Index" }
);

app.MapControllerRoute(
    name: "webapps_detail",
    pattern: "webapps/{slug}",
    defaults: new { controller = "WebApps", action = "Detail" }
);

app.MapControllerRoute(
    name: "webapps",
    pattern: "webapps",
    defaults: new { controller = "WebApps", action = "Index" }
);
app.MapControllerRoute(
    name: "homelab_detail",
    pattern: "homelab/{slug}",
    defaults: new { controller = "Homelab", action = "Detail" }
);

app.MapControllerRoute(
    name: "homelab",
    pattern: "homelab",
    defaults: new { controller = "Homelab", action = "Index" }
);

app.MapControllerRoute(
    name: "blog_detail",
    pattern: "blog/{slug}",
    defaults: new { controller = "Blog", action = "Detail" }
);

app.MapControllerRoute(
    name: "blog",
    pattern: "blog",
    defaults: new { controller = "Blog", action = "Index" }
);
app.MapControllerRoute(
    name: "team_detail",
    pattern: "team/{slug}",
    defaults: new { controller = "Team", action = "Detail" }
);

app.MapControllerRoute(
    name: "team",
    pattern: "team",
    defaults: new { controller = "Team", action = "Index" }
);

app.MapControllerRoute(
    name: "hire_contact",
    pattern: "hire/contact",
    defaults: new { controller = "Hire", action = "Contact" }
);
app.MapControllerRoute(
    name: "hire_track",
    pattern: "hire/track",
    defaults: new { controller = "Hire", action = "TrackTicket" }
);

app.MapControllerRoute(
    name: "hire",
    pattern: "hire",
    defaults: new { controller = "Hire", action = "Index" }
);

app.MapControllerRoute(
    name: "page_detail",
    pattern: "pages/{slug}",
    defaults: new { controller = "Page", action = "Detail" }
);

app.MapControllerRoute(
    name: "activity",
    pattern: "activity",
    defaults: new { controller = "Activity", action = "Index" }
);

app.MapControllerRoute(
    name: "search",
    pattern: "search",
    defaults: new { controller = "Search", action = "Index" }
);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);


app.Run();

static void SetHealthResponseHeaders(HttpContext context)
{
    SetPrivateResponseHeaders(context);
}

static void SetPrivateResponseHeaders(HttpContext context)
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
}

static bool HasValidInternalToken(
    HttpContext context,
    string? expectedToken)
{
    if (string.IsNullOrWhiteSpace(expectedToken) ||
        !context.Request.Headers.TryGetValue(
            "X-Emecworks-Report-Token",
            out var providedValues))
    {
        return false;
    }

    var providedToken = providedValues.ToString().Trim();
    if (providedToken.Length != expectedToken.Length)
    {
        return false;
    }

    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(providedToken),
        Encoding.UTF8.GetBytes(expectedToken));
}
