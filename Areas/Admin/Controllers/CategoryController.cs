using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;

namespace Portfolio.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class CategoryController : AdminBaseController
{

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
    public async Task<IActionResult> Create(Category model)
    {
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
    public async Task<IActionResult> Edit(int id, Category model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);

        // Load the existing database record.
        var existing = await _db.Categories.FindAsync(id);
        if (existing == null) return NotFound();

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

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}
