using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.Enums;
using Portfolio.Models.ViewModels;
using Portfolio.Services;

namespace Portfolio.Controllers;

public class HomeController : BaseController
{
    private readonly IActivityService _activity;

    public HomeController(AppDbContext db, IActivityService activity) : base(db)
    {
        _activity = activity;
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

        ViewBag.CurrentlyWorking = await _db.Notes
            .Where(n => n.IsTodo && !n.IsCompleted &&
                   (n.Priority == NotePriority.High || n.Priority == NotePriority.Critical))
            .OrderByDescending(n => n.CreatedAt)
            .FirstOrDefaultAsync();

        ViewBag.Certificates = await _db.Certificates
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        // Timeline widget — son 6 aktivite
        ViewBag.RecentActivity = await _activity.GetRecentActivityAsync(_db, 6);

        return View(model);
    }
}