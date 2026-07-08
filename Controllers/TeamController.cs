using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.ExtraData;
using Portfolio.Services;
using System.Text.Json;

namespace Portfolio.Controllers;

public class TeamController : Controller
{
    private readonly AppDbContext _db;
    private readonly IViewCountService _viewCount;

    public TeamController(AppDbContext db, IViewCountService viewCount)
    {
        _db = db;
        _viewCount = viewCount;
    }

    public async Task<IActionResult> Index()
    {
        var projects = await _db.TeamProjects
            .Include(t => t.Category)
            .OrderByDescending(t => t.EventDate)
            .ToListAsync();

        return View(projects);
    }

    public async Task<IActionResult> Detail(string slug)
    {
        var project = await _db.TeamProjects
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Slug == slug);

        if (project == null) return NotFound();

        await _viewCount.IncrementAsync("TeamProjects", project.Id);

        ViewBag.ContentHtml = Markdown.ToHtml(project.Content ?? "",
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());

        var members = new List<TeamMember>();
        if (!string.IsNullOrEmpty(project.TeamMembers))
            members = JsonSerializer.Deserialize<List<TeamMember>>(project.TeamMembers) ?? members;
        ViewBag.Members = members;

        ViewBag.Images = await _db.Media
            .Where(m => m.EntityType == "team_project" && m.EntityId == project.Id)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();

        return View(project);
    }
}