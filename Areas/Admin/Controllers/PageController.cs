using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Services;

namespace Portfolio.Areas.Admin.Controllers;

public class PageController : AdminBaseController
{
    private readonly ISlugService _slugService;

    public PageController(AppDbContext db, ISlugService slugService) : base(db)
    {
        _slugService = slugService;
    }

    public async Task<IActionResult> Index()
    {
        var pages = await _db.Pages
            .IgnoreQueryFilters()
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        return View(pages);
    }

    public IActionResult Create() => View(new Page());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Page model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            TempData["Error"] = "Başlık zorunlu.";
            return View(model);
        }

        model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "Pages");
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        _db.Pages.Add(model);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Sayfa oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var page = await _db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (page == null) return NotFound();
        return View(page);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Page model)
    {
        var existing = await _db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (existing == null) return NotFound();

        if (string.IsNullOrWhiteSpace(model.Title))
        {
            TempData["Error"] = "Başlık zorunlu.";
            return View(model);
        }

        existing.Title = model.Title;
        existing.Content = model.Content;
        existing.CoverImageUrl = model.CoverImageUrl;
        existing.Status = model.Status;
        existing.ShowInNav = model.ShowInNav;
        existing.SortOrder = model.SortOrder;
        existing.UpdatedAt = DateTime.UtcNow;

        if (existing.Title != model.Title)
            existing.Slug = await _slugService.GenerateUniqueAsync(model.Title, "Pages", id);

        await _db.SaveChangesAsync();

        TempData["Success"] = "Sayfa güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var page = await _db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (page == null) return NotFound();

        page.Status = page.Status == VisibilityStatus.Public
            ? VisibilityStatus.Draft
            : VisibilityStatus.Public;

        page.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var page = await _db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (page == null) return NotFound();

        _db.Pages.Remove(page);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}