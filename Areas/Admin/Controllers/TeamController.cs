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
    private readonly ILogger<TeamController> _logger;

    public TeamController(
        AppDbContext db,
        ISlugService slugService,
        IMediaService media,
        ILogger<TeamController> logger) : base(db)
    {
        _slugService = slugService;
        _media = media;
        _logger = logger;
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
    public async Task<IActionResult> Create(
    [Bind("Id,Title,Summary,Content,EventName,EventDate,EventUrl,MyRole,Outcome,GithubUrl,LiveDemoUrl,IsFeatured,Status")] TeamProject model,
    string? TeamMembersJson, List<IFormFile>? Images)
    {
        AdminContentValidator.ValidateTeam(ModelState, model, _slugService);
        await AdminContentValidator.ValidateImagesAsync(ModelState, _media, Images);

        var membersAreValid = TeamMemberJsonService.TryNormalize(
            TeamMembersJson, out var submittedMembers, out var normalizedTeamMembers);
        if (!membersAreValid)
        {
            ModelState.AddModelError(string.Empty, "Team member details or links are invalid.");
            TeamMemberJsonService.TryPrepareForEditing(TeamMembersJson, out submittedMembers);
        }

        ViewBag.Members = submittedMembers;

        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .Select(x => $"{x.Key}: {string.Join(", ", x.Value!.Errors.Select(e => e.ErrorMessage))}")
                .ToList();

            TempData["Error"] = "Form errors: " + string.Join(" | ", errors);
            return View(model);
        }

        try
        {
            var category = await _db.Categories.FirstOrDefaultAsync(c => c.Slug == "team");
            if (category == null)
            {
                TempData["Error"] = "The team category could not be found. Create a category with the 'team' slug first.";
                return View(model);
            }

            model.CategoryId = category.Id;
            model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "TeamProjects");
            model.CreatedAt = DateTime.UtcNow;
            model.UpdatedAt = DateTime.UtcNow;
            model.PublishedAt = model.Status == VisibilityStatus.Public ? DateTime.UtcNow : null;

            if (normalizedTeamMembers != null)
                model.TeamMembers = normalizedTeamMembers;

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

            TempData["Success"] = "The team project was created.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Creating a team project failed.");
            TempData["Error"] = "The team project could not be created. Review the form and try again.";
            return View(model);
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var project = await _db.TeamProjects
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id);

        if (project == null) return NotFound();

        // Parse team members.
        TeamMemberJsonService.TryNormalize(project.TeamMembers, out var members, out _);

        ViewBag.Members = members;
        ViewBag.Images = await _media.GetByEntityAsync("team_project", id);

        return View(project);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Id,Title,Summary,Content,EventName,EventDate,EventUrl,MyRole,Outcome,GithubUrl,LiveDemoUrl,IsFeatured,Status")] TeamProject model,
        string? TeamMembersJson, List<IFormFile>? Images)
    {
        var existing = await _db.TeamProjects.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (existing == null) return NotFound();

        AdminContentValidator.ValidateTeam(ModelState, model, _slugService);
        await AdminContentValidator.ValidateImagesAsync(ModelState, _media, Images);
        var membersAreValid = TeamMemberJsonService.TryNormalize(
            TeamMembersJson, out var submittedMembers, out var normalizedTeamMembers);
        if (!membersAreValid)
        {
            ModelState.AddModelError(string.Empty, "Team member details or links are invalid.");
            TeamMemberJsonService.TryPrepareForEditing(TeamMembersJson, out submittedMembers);
        }

        if (!ModelState.IsValid)
        {
            model.Id = id;
            ViewBag.Members = submittedMembers;
            ViewBag.Images = await _media.GetByEntityAsync("team_project", id);
            return View(model);
        }

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

        if (!string.IsNullOrWhiteSpace(TeamMembersJson))
            existing.TeamMembers = normalizedTeamMembers;

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
        if (!await _media.DeleteAsync(mediaId, "team_project", projectId))
            return NotFound();

        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCover(int mediaId, int projectId)
    {
        if (!await _media.SetCoverAsync(mediaId, "team_project", projectId))
            return NotFound();

        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var project = await _db.TeamProjects.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id);
        if (project == null) return NotFound();

        if (project.Status != VisibilityStatus.Public &&
            !AdminPublishValidator.CanPublishTeam(ModelState, project, _slugService))
        {
            TempData["Error"] = "This team project cannot be published yet. Open Edit and correct the highlighted fields.";
            return RedirectToAction(nameof(Index));
        }

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
