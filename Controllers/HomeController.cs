using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.Enums;
using Portfolio.Models.ViewModels;

namespace Portfolio.Controllers;

public class HomeController : BaseController
{

    public HomeController(AppDbContext db) : base(db)
    {
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

        return View(model);
    }
}