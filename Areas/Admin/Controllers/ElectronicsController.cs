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

    // Liste
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

    // Yeni form
    public IActionResult Create() => View(new Project());

    // Yeni kaydet
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Project model,
        string? Microcontroller, string? Components, string? SchematicUrl,
        string? ProgrammingLanguage, bool IsOpenSource,
        List<IFormFile>? Images)
    {
        if (!ModelState.IsValid)      // ← bunu ekle
            return View(model);
        // Elektronik kategorisini bul
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Slug == "electronics");
        if (category == null)
        {
            ModelState.AddModelError("", "Elektronik kategorisi bulunamadı.");
            return View(model);
        }

        model.CategoryId = category.Id;
        model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "Projects");
        model.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        // Bileşenleri virgülle ayrılmış string'den listeye çevir
        var components = Components?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .ToList();

        // ExtraData JSONB
        model.ExtraData = JsonSerializer.Serialize(new ElectronicsExtraData
        {
            Microcontroller = Microcontroller,
            Components = components,
            SchematicUrl = SchematicUrl,
            ProgrammingLanguage = ProgrammingLanguage,
            IsOpenSource = IsOpenSource
        });

        _db.Projects.Add(model);
        await _db.SaveChangesAsync();

        // Görselleri kaydet
        if (Images != null && Images.Any())
        {
            foreach (var file in Images)
            {
                if (file.Length > 0)
                    await _media.SaveAsync(file, "project", model.Id);
            }

            // İlk görseli cover yap
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

    // Düzenle formu
    public async Task<IActionResult> Edit(int id)
    {
        var project = await _db.Projects
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project == null) return NotFound();

        // ExtraData'yı parse et
        var extra = new ElectronicsExtraData();
        if (!string.IsNullOrEmpty(project.ExtraData))
            extra = JsonSerializer.Deserialize<ElectronicsExtraData>(project.ExtraData) ?? extra;

        ViewBag.Extra = extra;
        ViewBag.Images = await _media.GetByEntityAsync("project", id);

        return View(project);
    }

    // Düzenle kaydet
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Project model,
        string? Microcontroller, string? Components, string? SchematicUrl,
        string? ProgrammingLanguage, bool IsOpenSource,
        List<IFormFile>? Images)
    {
        var existing = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (existing == null) return NotFound();

        existing.Title = model.Title;
        existing.Summary = model.Summary;
        existing.Content = model.Content;
        existing.LiveDemoUrl = model.LiveDemoUrl;
        existing.GithubUrl = model.GithubUrl;
        existing.Status = model.Status;
        existing.IsFeatured = model.IsFeatured;
        existing.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        existing.UpdatedAt = DateTime.UtcNow;

        // Slug sadece başlık değiştiyse güncelle
        if (existing.Title != model.Title)
            existing.Slug = await _slugService.GenerateUniqueAsync(model.Title, "Projects", id);

        var components = Components?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .ToList();

        existing.ExtraData = JsonSerializer.Serialize(new ElectronicsExtraData
        {
            Microcontroller = Microcontroller,
            Components = components,
            SchematicUrl = SchematicUrl,
            ProgrammingLanguage = ProgrammingLanguage,
            IsOpenSource = IsOpenSource
        });

        await _db.SaveChangesAsync();

        // Yeni görseller
        if (Images != null && Images.Any())
        {
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "project", id);
        }

        return RedirectToAction(nameof(Index));
    }

    // Görsel sil
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int mediaId, int projectId)
    {
        await _media.DeleteAsync(mediaId);
        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    // Cover seç
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCover(int mediaId, int projectId)
    {
        await _media.SetCoverAsync(mediaId, "project", projectId);

        var media = await _db.Media.FindAsync(mediaId);
        var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == projectId);
        if (project != null && media != null)
        {
            project.CoverImageUrl = media.Url;
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Edit), new { id = projectId });
    }

    // Görünürlük toggle
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        project.Status = project.Status == VisibilityStatus.Public
            ? VisibilityStatus.Draft
            : VisibilityStatus.Public;

        if (project.Status == VisibilityStatus.Public && project.PublishedAt == null)
            project.PublishedAt = DateTime.UtcNow;

        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // Sil
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (project == null) return NotFound();

        // Önce görselleri sil
        var images = await _media.GetByEntityAsync("project", id);
        foreach (var img in images)
            await _media.DeleteAsync(img.Id);

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}