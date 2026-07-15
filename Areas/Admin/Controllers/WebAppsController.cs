using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Models.ExtraData;
using Portfolio.Services;
using System.Text.Json;

namespace Portfolio.Areas.Admin.Controllers;

public class WebAppsController : AdminBaseController
{
    private readonly ISlugService _slugService;
    private readonly IReadingTimeService _readingTime;
    private readonly IMediaService _media;

    public WebAppsController(AppDbContext db, ISlugService slugService,
        IReadingTimeService readingTime, IMediaService media) : base(db)
    {
        _slugService = slugService;
        _readingTime = readingTime;
        _media = media;
    }

    public async Task<IActionResult> Index()
    {
        var projects = await _db.Projects
            .IgnoreQueryFilters()
            .Include(p => p.Category)
            .Where(p => p.Category.Slug == "webapps")
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return View(projects);
    }

    public IActionResult Create() => View(new Project());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Id,Title,Summary,Content,LiveDemoUrl,GithubUrl,IsFeatured,Status")] Project model,
        string? TechStack, int? TeamSize, string? MyRole,
        string? Subdomain, bool IsSchoolProject,
        List<IFormFile>? Images)
    {
        AdminContentValidator.ValidateProject(ModelState, model, _slugService);
        AdminContentValidator.ValidateWebAppFields(ModelState, TechStack, TeamSize, MyRole, Subdomain);
        await AdminContentValidator.ValidateImagesAsync(ModelState, _media, Images);

        if (!ModelState.IsValid)
            return View(model);

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Slug == "webapps");
        if (category == null)
        {
            ModelState.AddModelError("", "The web applications category could not be found.");
            return View(model);
        }

        model.CategoryId = category.Id;
        model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "Projects");
        model.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;
        model.PublishedAt = model.Status == VisibilityStatus.Public ? DateTime.UtcNow : null;

        var techStack = TechStack?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .ToList();

        model.ExtraData = JsonSerializer.Serialize(new WebAppExtraData
        {
            TechStack = techStack,
            TeamSize = TeamSize,
            MyRole = string.IsNullOrWhiteSpace(MyRole) ? null : MyRole.Trim(),
            Subdomain = string.IsNullOrWhiteSpace(Subdomain) ? null : Subdomain.Trim().ToLowerInvariant(),
            IsSchoolProject = IsSchoolProject
        });

        _db.Projects.Add(model);
        await _db.SaveChangesAsync();

        if (Images != null && Images.Any())
        {
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "project", model.Id);

            var firstMedia = await _db.Media
                .FirstOrDefaultAsync(m => m.EntityType == "project" && m.EntityId == model.Id);
            if (firstMedia != null)
            {
                firstMedia.IsCover = true;
                model.CoverImageUrl = firstMedia.Url;
                await _db.SaveChangesAsync();
            }
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var project = await _db.Projects
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project == null) return NotFound();

        var extra = new WebAppExtraData();
        if (!string.IsNullOrEmpty(project.ExtraData))
            extra = JsonSerializer.Deserialize<WebAppExtraData>(project.ExtraData) ?? extra;

        ViewBag.Extra = extra;
        ViewBag.Images = await _media.GetByEntityAsync("project", id);

        return View(project);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Id,Title,Summary,Content,LiveDemoUrl,GithubUrl,IsFeatured,Status")] Project model,
        string? TechStack, int? TeamSize, string? MyRole,
        string? Subdomain, bool IsSchoolProject,
        List<IFormFile>? Images)
    {
        var existing = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (existing == null) return NotFound();

        AdminContentValidator.ValidateProject(ModelState, model, _slugService);
        AdminContentValidator.ValidateWebAppFields(ModelState, TechStack, TeamSize, MyRole, Subdomain);
        await AdminContentValidator.ValidateImagesAsync(ModelState, _media, Images);

        if (!ModelState.IsValid)
        {
            model.Id = id;
            ViewBag.Extra = new WebAppExtraData
            {
                TechStack = TechStack?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                TeamSize = TeamSize,
                MyRole = MyRole,
                Subdomain = Subdomain,
                IsSchoolProject = IsSchoolProject
            };
            ViewBag.Images = await _media.GetByEntityAsync("project", id);
            return View(model);
        }

        if (existing.Title != model.Title)
            existing.Slug = await _slugService.GenerateUniqueAsync(model.Title, "Projects", id);

        existing.Title = model.Title;
        existing.Summary = model.Summary;
        existing.Content = model.Content;
        existing.LiveDemoUrl = model.LiveDemoUrl;
        existing.GithubUrl = model.GithubUrl;
        existing.Status = model.Status;
        if (existing.Status == VisibilityStatus.Public && existing.PublishedAt == null)
            existing.PublishedAt = DateTime.UtcNow;

        existing.IsFeatured = model.IsFeatured;
        existing.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        existing.UpdatedAt = DateTime.UtcNow;

        var techStack = TechStack?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .ToList();

        existing.ExtraData = JsonSerializer.Serialize(new WebAppExtraData
        {
            TechStack = techStack,
            TeamSize = TeamSize,
            MyRole = string.IsNullOrWhiteSpace(MyRole) ? null : MyRole.Trim(),
            Subdomain = string.IsNullOrWhiteSpace(Subdomain) ? null : Subdomain.Trim().ToLowerInvariant(),
            IsSchoolProject = IsSchoolProject
        });

        await _db.SaveChangesAsync();

        if (Images != null && Images.Any())
        {
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "project", id);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int mediaId, int projectId)
    {
        if (!await _media.DeleteAsync(mediaId, "project", projectId))
            return NotFound();

        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCover(int mediaId, int projectId)
    {
        if (!await _media.SetCoverAsync(mediaId, "project", projectId))
            return NotFound();

        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        if (project.Status != VisibilityStatus.Public &&
            !AdminPublishValidator.CanPublishWebApp(ModelState, project, _slugService))
        {
            TempData["Error"] = "This project cannot be published yet. Open Edit and correct the highlighted fields.";
            return RedirectToAction(nameof(Index));
        }

        project.Status = project.Status == VisibilityStatus.Public
            ? VisibilityStatus.Draft
            : VisibilityStatus.Public;

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
        var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        var images = await _media.GetByEntityAsync("project", id);
        foreach (var img in images)
            await _media.DeleteAsync(img.Id);

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}
