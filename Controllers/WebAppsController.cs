using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.ExtraData;
using Portfolio.Services;
using System.Text.Json;

namespace Portfolio.Controllers;

public class WebAppsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IViewCountService _viewCount;

    public WebAppsController(AppDbContext db, IViewCountService viewCount)
    {
        _db = db;
        _viewCount = viewCount;
    }

    public async Task<IActionResult> Index()
    {
        var projects = await _db.Projects
            .Include(p => p.Category)
            .Where(p => p.Category.Slug == "webapps")
            .OrderByDescending(p => p.PublishedAt)
            .ToListAsync();

        return View(projects);
    }

    public async Task<IActionResult> Detail(string slug)
    {
        var project = await _db.Projects
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Slug == slug && p.Category.Slug == "webapps");

        if (project == null) return NotFound();

        await _viewCount.IncrementAsync("Projects", project.Id);

        ViewBag.ContentHtml = Markdown.ToHtml(project.Content ?? "",
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());

        var extra = new WebAppExtraData();
        if (!string.IsNullOrEmpty(project.ExtraData))
            extra = JsonSerializer.Deserialize<WebAppExtraData>(project.ExtraData) ?? extra;
        ViewBag.Extra = extra;

        ViewBag.Images = await _db.Media
            .Where(m => m.EntityType == "project" && m.EntityId == project.Id)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();

        ViewBag.Related = await _db.Projects
            .Include(p => p.Category)
            .Where(p => p.Category.Slug == "webapps" && p.Id != project.Id)
            .OrderByDescending(p => p.PublishedAt)
            .Take(3)
            .ToListAsync();

        return View(project);
    }
}