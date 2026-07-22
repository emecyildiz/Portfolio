using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Models.ExtraData;
using Portfolio.Services;
using System.Text.Json;


namespace Portfolio.Areas.Admin.Controllers;

public class ElectronicsController : AdminBaseController
{
    private readonly ISlugService _slugService;
    private readonly IReadingTimeService _readingTime;
    private readonly IMediaService _media;

    public ElectronicsController(AppDbContext db, ISlugService slugService,
        IReadingTimeService readingTime, IMediaService media) : base(db)
    {
        _slugService = slugService;
        _readingTime = readingTime;
        _media = media;
    }

    // List
    public async Task<IActionResult> Index()
    {
        var projects = await _db.Projects
            .IgnoreQueryFilters()
            .Include(p => p.Category)
            .Where(p => p.Category.Slug == "electronics")
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

        return View(projects);
    }

    // New project form
    public IActionResult Create() => View(new Project());

    // Save a new project
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Id,Title,Summary,Content,LiveDemoUrl,GithubUrl,IsFeatured,Status")] Project model,
        string? Microcontroller, string? Components, string? SchematicUrl,
        string? ProgrammingLanguage, bool IsOpenSource,
        List<IFormFile>? Images)
    {
        ViewBag.Extra = BuildSubmittedExtraData(
            Microcontroller, Components, SchematicUrl, ProgrammingLanguage, IsOpenSource);

        AdminContentValidator.ValidateProject(ModelState, model, _slugService);
        AdminContentValidator.ValidateElectronicsFields(
            ModelState, Microcontroller, Components, SchematicUrl, ProgrammingLanguage);
        await AdminContentValidator.ValidateImagesAsync(ModelState, _media, Images);

        if (!ModelState.IsValid)
            return View(model);
        // Find the electronics category.
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Slug == "electronics");
        if (category == null)
        {
            ModelState.AddModelError("", "The electronics category could not be found.");
            return View(model);
        }

        model.CategoryId = category.Id;
        model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "Projects");
        model.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;
        model.PublishedAt = model.Status == VisibilityStatus.Public ? DateTime.UtcNow : null;

        // Convert the comma-separated components string into a list.
        var components = Components?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .ToList();

        // ExtraData JSONB
        model.ExtraData = JsonSerializer.Serialize(new ElectronicsExtraData
        {
            Microcontroller = string.IsNullOrWhiteSpace(Microcontroller) ? null : Microcontroller.Trim(),
            Components = components,
            SchematicUrl = string.IsNullOrWhiteSpace(SchematicUrl) ? null : SchematicUrl.Trim(),
            ProgrammingLanguage = string.IsNullOrWhiteSpace(ProgrammingLanguage) ? null : ProgrammingLanguage.Trim(),
            IsOpenSource = IsOpenSource
        });

        _db.Projects.Add(model);
        await _db.SaveChangesAsync();

        // Save images.
        if (Images != null && Images.Any())
        {
            foreach (var file in Images)
            {
                if (file.Length > 0)
                    await _media.SaveAsync(file, "project", model.Id);
            }

            // Use the first image as the cover.
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

    // Edit form
    public async Task<IActionResult> Edit(int id)
    {
        var project = await _db.Projects
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project == null) return NotFound();

        // Parse the category-specific extra data.
        var extra = new ElectronicsExtraData();
        if (!string.IsNullOrEmpty(project.ExtraData))
            extra = JsonSerializer.Deserialize<ElectronicsExtraData>(project.ExtraData) ?? extra;

        ViewBag.Extra = extra;
        ViewBag.Images = await _media.GetByEntityAsync("project", id);

        return View(project);
    }

    // Save changes
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Id,Title,Summary,Content,LiveDemoUrl,GithubUrl,IsFeatured,Status")] Project model,
        string? Microcontroller, string? Components, string? SchematicUrl,
        string? ProgrammingLanguage, bool IsOpenSource,
        List<IFormFile>? Images)
    {
        var existing = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (existing == null) return NotFound();

        AdminContentValidator.ValidateProject(ModelState, model, _slugService);
        AdminContentValidator.ValidateElectronicsFields(
            ModelState, Microcontroller, Components, SchematicUrl, ProgrammingLanguage);
        await AdminContentValidator.ValidateImagesAsync(ModelState, _media, Images);

        if (!ModelState.IsValid)
        {
            model.Id = id;
            ViewBag.Extra = BuildSubmittedExtraData(
                Microcontroller, Components, SchematicUrl, ProgrammingLanguage, IsOpenSource);
            ViewBag.Images = await _media.GetByEntityAsync("project", id);
            return View(model);
        }

        // Update the slug only when the title changes.
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

        var components = Components?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .ToList();

        existing.ExtraData = JsonSerializer.Serialize(new ElectronicsExtraData
        {
            Microcontroller = string.IsNullOrWhiteSpace(Microcontroller) ? null : Microcontroller.Trim(),
            Components = components,
            SchematicUrl = string.IsNullOrWhiteSpace(SchematicUrl) ? null : SchematicUrl.Trim(),
            ProgrammingLanguage = string.IsNullOrWhiteSpace(ProgrammingLanguage) ? null : ProgrammingLanguage.Trim(),
            IsOpenSource = IsOpenSource
        });

        await _db.SaveChangesAsync();

        // New images
        if (Images != null && Images.Any())
        {
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "project", id);
        }

        return RedirectToAction(nameof(Index));
    }

    private static ElectronicsExtraData BuildSubmittedExtraData(
        string? microcontroller,
        string? components,
        string? schematicUrl,
        string? programmingLanguage,
        bool isOpenSource) => new()
        {
            Microcontroller = microcontroller,
            Components = components?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            SchematicUrl = schematicUrl,
            ProgrammingLanguage = programmingLanguage,
            IsOpenSource = isOpenSource
        };

    // Delete an image
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int mediaId, int projectId)
    {
        if (!await _media.DeleteAsync(mediaId, "project", projectId))
            return NotFound();

        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    // Select a cover image
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCover(int mediaId, int projectId)
    {
        if (!await _media.SetCoverAsync(mediaId, "project", projectId))
            return NotFound();

        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    // Toggle visibility
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        if (project.Status != VisibilityStatus.Public &&
            !AdminPublishValidator.CanPublishElectronics(ModelState, project, _slugService))
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

    // Delete
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        // Delete related images first.
        var images = await _media.GetByEntityAsync("project", id);
        foreach (var img in images)
            await _media.DeleteAsync(img.Id);

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}
