using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Models.ExtraData;
using Portfolio.Services;
using System.Text.Json;

namespace Portfolio.Areas.Admin.Controllers;

public class TeamController : AdminBaseController
{
    private readonly ISlugService _slugService;
    private readonly IMediaService _media;

    public TeamController(AppDbContext db, ISlugService slugService, IMediaService media) : base(db)
    {
        _slugService = slugService;
        _media = media;
    }

    public async Task<IActionResult> Index()
    {
        var projects = await _db.TeamProjects
            .IgnoreQueryFilters()
            .Include(t => t.Category)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return View(projects);
    }

    public IActionResult Create() => View(new TeamProject());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TeamProject model,
    string? TeamMembersJson, List<IFormFile>? Images)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .Select(x => $"{x.Key}: {string.Join(", ", x.Value!.Errors.Select(e => e.ErrorMessage))}")
                .ToList();

            TempData["Error"] = "Form hataları: " + string.Join(" | ", errors);
            return View(model);
        }

        try
        {
            var category = await _db.Categories.FirstOrDefaultAsync(c => c.Slug == "team");
            if (category == null)
            {
                TempData["Error"] = "Ekip kategorisi bulunamadı. Önce 'team' slug'ına sahip kategori oluştur.";
                return View(model);
            }

            model.CategoryId = category.Id;
            model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "TeamProjects");
            model.CreatedAt = DateTime.UtcNow;
            model.UpdatedAt = DateTime.UtcNow;
            model.PublishedAt = model.Status == VisibilityStatus.Public ? DateTime.UtcNow : null;

            if (!string.IsNullOrEmpty(TeamMembersJson))
                model.TeamMembers = TeamMembersJson;

            _db.TeamProjects.Add(model);
            await _db.SaveChangesAsync();

            if (Images != null && Images.Any())
            {
                foreach (var file in Images.Where(f => f.Length > 0))
                    await _media.SaveAsync(file, "team_project", model.Id);

                var firstMedia = await _db.Media
                    .FirstOrDefaultAsync(m => m.EntityType == "team_project" && m.EntityId == model.Id);
                if (firstMedia != null)
                {
                    firstMedia.IsCover = true;
                    model.CoverImageUrl = firstMedia.Url;
                    await _db.SaveChangesAsync();
                }
            }

            TempData["Success"] = "Ekip projesi oluşturuldu.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Hata: {ex.Message}";
            return View(model);
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var project = await _db.TeamProjects
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (project == null) return NotFound();

        // Ekip üyelerini parse et
        var members = new List<TeamMember>();
        if (!string.IsNullOrEmpty(project.TeamMembers))
            members = JsonSerializer.Deserialize<List<TeamMember>>(project.TeamMembers) ?? members;

        ViewBag.Members = members;
        ViewBag.Images = await _media.GetByEntityAsync("team_project", id);

        return View(project);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, TeamProject model,
        string? TeamMembersJson, List<IFormFile>? Images)
    {
        var existing = await _db.TeamProjects.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (existing == null) return NotFound();

        if (existing.Title != model.Title)
            existing.Slug = await _slugService.GenerateUniqueAsync(model.Title, "TeamProjects", id);

        existing.Title      = model.Title;
        existing.Summary    = model.Summary;
        existing.Content    = model.Content;
        existing.EventName  = model.EventName;
        existing.EventDate  = model.EventDate;
        existing.EventUrl   = model.EventUrl;
        existing.MyRole     = model.MyRole;
        existing.Outcome    = model.Outcome;
        existing.GithubUrl  = model.GithubUrl;
        existing.LiveDemoUrl = model.LiveDemoUrl;
        existing.Status     = model.Status;
        if (existing.Status == VisibilityStatus.Public && existing.PublishedAt == null)
            existing.PublishedAt = DateTime.UtcNow;

        existing.IsFeatured = model.IsFeatured;
        existing.UpdatedAt  = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(TeamMembersJson))
            existing.TeamMembers = TeamMembersJson;

        await _db.SaveChangesAsync();

        if (Images != null && Images.Any())
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "team_project", id);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int mediaId, int projectId)
    {
        await _media.DeleteAsync(mediaId);
        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCover(int mediaId, int projectId)
    {
        await _media.SetCoverAsync(mediaId, "team_project", projectId);
        var media = await _db.Media.FindAsync(mediaId);
        var project = await _db.TeamProjects.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == projectId);
        if (project != null && media != null)
        {
            project.CoverImageUrl = media.Url;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var project = await _db.TeamProjects.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (project == null) return NotFound();

        project.Status = project.Status == VisibilityStatus.Public
            ? VisibilityStatus.Draft : VisibilityStatus.Public;

        if (project.Status == VisibilityStatus.Public && project.PublishedAt == null)
            project.PublishedAt = DateTime.UtcNow;

        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var project = await _db.TeamProjects.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (project == null) return NotFound();

        var images = await _media.GetByEntityAsync("team_project", id);
        foreach (var img in images)
            await _media.DeleteAsync(img.Id);

        _db.TeamProjects.Remove(project);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
