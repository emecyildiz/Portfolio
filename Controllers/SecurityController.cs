using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.Enums;
using Portfolio.Services;

namespace Portfolio.Controllers;

public class SecurityController : BaseController
{
    private readonly IViewCountService _viewCount;

    public SecurityController(AppDbContext db, IViewCountService viewCount) : base(db)
    {
        _viewCount = viewCount;
    }

    // Liste sayfası — /security
    public async Task<IActionResult> Index(string? type)
    {
        var category = await _db.Categories
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Slug == "security");

        if (category == null || category.Status != VisibilityStatus.Public)
            return View("CategoryUnavailable");

        var query = _db.SecurityResearches
            .Include(s => s.Category)
            .AsQueryable();

        // Tipe göre filtre
        if (!string.IsNullOrEmpty(type) && Enum.TryParse<ResearchType>(type, out var researchType))
            query = query.Where(s => s.ResearchType == researchType);

        var researches = await query
            .OrderByDescending(s => s.PublishedAt)
            .ToListAsync();

        ViewBag.CurrentType = type;
        return View(researches);
    }

    // Detay sayfası — /security/{slug}
    public async Task<IActionResult> Detail(string slug)
    {
        var category = await _db.Categories
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Slug == "security");

        if (category == null || category.Status != VisibilityStatus.Public)
            return View("CategoryUnavailable");

        var research = await _db.SecurityResearches
            .Include(s => s.Category)
            .FirstOrDefaultAsync(s => s.Slug == slug);

        if (research == null) return NotFound();

        // View count artır
        await _viewCount.IncrementAsync("SecurityResearches", research.Id);

        // Markdown'ı HTML'e çevir
        ViewBag.ContentHtml = Markdown.ToHtml(research.Content ?? "",
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());

        // İlgili araştırmalar — aynı tip
        ViewBag.Related = await _db.SecurityResearches
            .Where(s => s.ResearchType == research.ResearchType && s.Id != research.Id)
            .OrderByDescending(s => s.PublishedAt)
            .Take(3)
            .ToListAsync();

        // Görseller
        ViewBag.Images = await _db.Media
            .Where(m => m.EntityType == "security_research" && m.EntityId == research.Id)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();

        ViewBag.OgImage = research.CoverImageUrl;

        return View(research);
    }
}