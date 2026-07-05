using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Services;
using System.Text.Json;

namespace Portfolio.Areas.Admin.Controllers;

public class SecurityController : AdminBaseController
{
    private readonly ISlugService _slugService;
    private readonly IReadingTimeService _readingTime;
    private readonly IMediaService _media;

    public SecurityController(AppDbContext db, ISlugService slugService,
        IReadingTimeService readingTime, IMediaService media) : base(db)
    {
        _slugService = slugService;
        _readingTime = readingTime;
        _media = media;
    }

    public async Task<IActionResult> Index()
    {
        var researches = await _db.SecurityResearches
            .IgnoreQueryFilters()
            .Include(s => s.Category)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return View(researches);
    }

    public IActionResult Create() => View(new SecurityResearch());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SecurityResearch model,
        string? ToolsUsed, List<IFormFile>? Images)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Slug == "security");
        if (category == null)
        {
            ModelState.AddModelError("", "Güvenlik kategorisi bulunamadı.");
            return View(model);
        }

        model.CategoryId = category.Id;
        model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "SecurityResearches");
        model.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        // Araçları JSON listesine çevir
        if (!string.IsNullOrEmpty(ToolsUsed))
        {
            var tools = ToolsUsed
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToList();
            model.ToolsUsed = JsonSerializer.Serialize(tools);
        }

        _db.SecurityResearches.Add(model);
        await _db.SaveChangesAsync();

        if (Images != null && Images.Any())
        {
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "security_research", model.Id);

            var firstMedia = await _db.Media
                .FirstOrDefaultAsync(m => m.EntityType == "security_research" && m.EntityId == model.Id);
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
        var research = await _db.SecurityResearches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id);

        if (research == null) return NotFound();

        // ToolsUsed JSON'dan string'e çevir
        var toolsString = "";
        if (!string.IsNullOrEmpty(research.ToolsUsed))
        {
            var tools = JsonSerializer.Deserialize<List<string>>(research.ToolsUsed);
            toolsString = string.Join(", ", tools ?? new List<string>());
        }

        ViewBag.ToolsString = toolsString;
        ViewBag.Images = await _media.GetByEntityAsync("security_research", id);

        return View(research);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SecurityResearch model,
        string? ToolsUsed, List<IFormFile>? Images)
    {
        var existing = await _db.SecurityResearches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (existing == null) return NotFound();

        existing.Title = model.Title;
        existing.Summary = model.Summary;
        existing.Content = model.Content;
        existing.ResearchType = model.ResearchType;
        existing.TargetCategory = model.TargetCategory;
        existing.CveId = model.CveId;
        existing.SeverityLevel = model.SeverityLevel;
        existing.DisclosureStatus = model.DisclosureStatus;
        existing.GithubUrl = model.GithubUrl;
        existing.Status = model.Status;
        existing.IsFeatured = model.IsFeatured;
        existing.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        existing.UpdatedAt = DateTime.UtcNow;

        if (existing.Title != model.Title)
            existing.Slug = await _slugService.GenerateUniqueAsync(model.Title, "SecurityResearches", id);

        if (!string.IsNullOrEmpty(ToolsUsed))
        {
            var tools = ToolsUsed
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .ToList();
            existing.ToolsUsed = JsonSerializer.Serialize(tools);
        }

        await _db.SaveChangesAsync();

        if (Images != null && Images.Any())
        {
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "security_research", id);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int mediaId, int researchId)
    {
        await _media.DeleteAsync(mediaId);
        return RedirectToAction(nameof(Edit), new { id = researchId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCover(int mediaId, int researchId)
    {
        await _media.SetCoverAsync(mediaId, "security_research", researchId);

        var media = await _db.Media.FindAsync(mediaId);
        var research = await _db.SecurityResearches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == researchId);

        if (research != null && media != null)
        {
            research.CoverImageUrl = media.Url;
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Edit), new { id = researchId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var research = await _db.SecurityResearches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (research == null) return NotFound();

        research.Status = research.Status == VisibilityStatus.Public
            ? VisibilityStatus.Draft
            : VisibilityStatus.Public;

        if (research.Status == VisibilityStatus.Public && research.PublishedAt == null)
            research.PublishedAt = DateTime.UtcNow;

        research.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var research = await _db.SecurityResearches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (research == null) return NotFound();

        var images = await _media.GetByEntityAsync("security_research", id);
        foreach (var img in images)
            await _media.DeleteAsync(img.Id);

        _db.SecurityResearches.Remove(research);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}