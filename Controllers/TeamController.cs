using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.Enums;
using Portfolio.Models.ExtraData;
using Portfolio.Services;
using System.Text.Json;

namespace Portfolio.Controllers;

public class TeamController : BaseController
{
    private readonly IViewCountService _viewCount;

    public TeamController(AppDbContext db, IViewCountService viewCount) : base(db)
    {
        _viewCount = viewCount;
    }

    public async Task<IActionResult> Index()
    {
        var category = await _db.Categories
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Slug == "team");

        if (category?.IsPrivate == true)
            return NotFound();

        if (category == null || category.Status != VisibilityStatus.Public)
            return View("CategoryUnavailable");

        var projects = await _db.TeamProjects
            .Include(t => t.Category)
            .OrderByDescending(t => t.EventDate)
            .ToListAsync();

        return View(projects);
    }

    public async Task<IActionResult> Detail(string slug)
    {
        var category = await _db.Categories
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Slug == "team");

        if (category?.IsPrivate == true)
            return NotFound();

        if (category == null || category.Status != VisibilityStatus.Public)
            return View("CategoryUnavailable");

        var project = await _db.TeamProjects
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Slug == slug);

        if (project == null) return NotFound();

        if (await _viewCount.TryIncrementUniqueAsync(
                "TeamProjects", project.Id, HttpContext, HttpContext.RequestAborted))
        {
            project.ViewCount++;
        }

        ViewBag.ContentHtml = MarkdownContentRenderer.ToHtml(project.Content);

        TeamMemberJsonService.TryNormalize(project.TeamMembers, out var members, out _);
        ViewBag.Members = members;

        ViewBag.Images = await _db.Media
            .Where(m => m.EntityType == "team_project" && m.EntityId == project.Id)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();

        ViewBag.OgImage = project.CoverImageUrl;

        return View(project);
    }
}
