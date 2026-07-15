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
    public async Task<IActionResult> Create(
        [Bind("Id,Title,Content,CoverImageUrl,Status,ShowInNav,SortOrder")] Page model)
    {
        AdminContentValidator.ValidatePage(ModelState, model, _slugService);
        if (!ModelState.IsValid)
            return View(model);

        model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "Pages");
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        _db.Pages.Add(model);
        await _db.SaveChangesAsync();

        TempData["Success"] = "The page was created.";
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
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Id,Title,Content,CoverImageUrl,Status,ShowInNav,SortOrder")] Page model)
    {
        var existing = await _db.Pages.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (existing == null) return NotFound();

        AdminContentValidator.ValidatePage(ModelState, model, _slugService);
        if (!ModelState.IsValid)
        {
            model.Id = id;
            return View(model);
        }

        if (existing.Title != model.Title)
            existing.Slug = await _slugService.GenerateUniqueAsync(model.Title, "Pages", id);

        existing.Title = model.Title;
        existing.Content = model.Content;
        existing.CoverImageUrl = model.CoverImageUrl;
        existing.Status = model.Status;
        existing.ShowInNav = model.ShowInNav;
        existing.SortOrder = model.SortOrder;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        TempData["Success"] = "The page was updated.";
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
