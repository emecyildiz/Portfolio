using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.ViewModels;
using Portfolio.Services;

namespace Portfolio.Controllers;

public class HomeController : BaseController
{
    private readonly IActivityService _activity;
    private readonly IConfiguration _configuration;

    public HomeController(
        AppDbContext db,
        IActivityService activity,
        IConfiguration configuration) : base(db)
    {
        _activity = activity;
        _configuration = configuration;
    }

    public async Task<IActionResult> Index()
    {
        var model = new HomepageViewModel
        {
            FeaturedProjects = await _db.Projects
                .Include(p => p.Category)
                .Where(p => p.IsFeatured)
                .OrderBy(p => p.SortOrder)
                .Take(6)
                .ToListAsync(),

            FeaturedSecurity = await _db.SecurityResearches
                .Where(s => s.IsFeatured)
                .OrderByDescending(s => s.PublishedAt)
                .Take(3)
                .ToListAsync(),

            FeaturedHomelab = await _db.HomelabPosts
                .Where(h => h.IsFeatured)
                .OrderByDescending(h => h.PublishedAt)
                .Take(3)
                .ToListAsync(),

            RecentBlog = await _db.BlogPosts
                .OrderByDescending(b => b.PublishedAt)
                .Take(4)
                .ToListAsync(),
        };

        model.CurrentFocus = await _db.SiteSettings
            .AsNoTracking()
            .Where(settings => settings.ShowCurrentFocus &&
                               settings.CurrentFocusTitle != null &&
                               settings.CurrentFocusTitle != string.Empty)
            .OrderBy(settings => settings.Id)
            .Select(settings => new CurrentFocusViewModel
            {
                Title = settings.CurrentFocusTitle!,
                Url = settings.CurrentFocusUrl
            })
            .FirstOrDefaultAsync();

        ViewBag.Certificates = await _db.Certificates
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        // Timeline widget - latest 6 activities
        ViewBag.RecentActivity = await _activity.GetRecentActivityAsync(_db, 6);

        return View(model);
    }

    [HttpGet("/privacy")]
    public IActionResult Privacy() => View();

    [HttpPost("/privacy/consent")]
    [ValidateAntiForgeryToken]
    public IActionResult SaveAnalyticsConsent(string decision, string? returnUrl)
    {
        if (decision is not (AnalyticsConsent.GrantedValue or AnalyticsConsent.DeniedValue))
            return BadRequest();

        var consentCookieDays = _configuration.GetValue("Privacy:ConsentCookieDays", 180);
        if (consentCookieDays is < 30 or > 365)
            consentCookieDays = 180;

        Response.Cookies.Append(
            AnalyticsConsent.CookieName,
            decision,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(consentCookieDays),
                MaxAge = TimeSpan.FromDays(consentCookieDays),
                IsEssential = true
            });

        if (decision == AnalyticsConsent.DeniedValue)
        {
            Response.Cookies.Delete(
                AnalyticsConsent.VisitorCookieName,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                    IsEssential = false
                });
        }

        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Privacy));
    }
}
