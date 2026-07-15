using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Services;

namespace Portfolio.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class CategoryController : AdminBaseController
{
    private static readonly HashSet<string> ProtectedCategorySlugs =
        ["security", "electronics", "webapps", "homelab", "blog", "team", "notes"];

    public CategoryController(AppDbContext db) : base(db) { }

    // List
    public async Task<IActionResult> Index()
    {
        var categories = await _db.Categories
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
        return View(categories);
    }

    // New category form
    public IActionResult Create() => View(new Category());

    // Save a new category
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Id,Name,Slug,Description,IconClass,SortOrder,IsPrivate,Status")] Category model)
    {
        AdminContentValidator.ValidateCategory(ModelState, model);

        if (await _db.Categories.AnyAsync(category => category.Slug == model.Slug))
            ModelState.AddModelError(nameof(model.Slug), "This slug is already in use.");

        if (!ModelState.IsValid)
            return View(model);

        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        _db.Categories.Add(model);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // Edit form
    public async Task<IActionResult> Edit(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound();
        return View(category);
    }

    // Save changes
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Id,Name,Slug,Description,IconClass,SortOrder,IsPrivate,Status")] Category model)
    {
        if (id != model.Id) return BadRequest();

        // Load the existing database record.
        var existing = await _db.Categories.FindAsync(id);
        if (existing == null) return NotFound();

        AdminContentValidator.ValidateCategory(ModelState, model);

        if (ProtectedCategorySlugs.Contains(existing.Slug) && existing.Slug != model.Slug)
            ModelState.AddModelError(nameof(model.Slug), "The slug of a built-in category cannot be changed.");

        if (await _db.Categories.AnyAsync(category => category.Id != id && category.Slug == model.Slug))
            ModelState.AddModelError(nameof(model.Slug), "This slug is already in use.");

        if (!ModelState.IsValid) return View(model);

        // Update only editable fields to preserve database-managed values.
        existing.Name = model.Name;
        existing.Slug = model.Slug;
        existing.Description = model.Description;
        existing.IconClass = model.IconClass;
        existing.SortOrder = model.SortOrder;
        existing.IsPrivate = model.IsPrivate;
        existing.Status = model.Status;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // Toggle visibility from the sidebar control.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound();

        category.Status = category.Status == VisibilityStatus.Public
            ? VisibilityStatus.Draft
            : VisibilityStatus.Public;

        category.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // Delete
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound();

        if (ProtectedCategorySlugs.Contains(category.Slug))
        {
            TempData["Error"] = "Built-in categories cannot be deleted because public routes depend on them.";
            return RedirectToAction(nameof(Index));
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}
