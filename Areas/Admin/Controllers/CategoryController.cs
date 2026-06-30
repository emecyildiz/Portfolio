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

    // Liste
    public async Task<IActionResult> Index()
    {
        var categories = await _db.Categories
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
        return View(categories);
    }

    // Yeni kategori formu
    public IActionResult Create() => View(new Category());

    // Yeni kategori kaydet
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

    // Düzenle formu
    public async Task<IActionResult> Edit(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound();
        return View(category);
    }

    // Düzenle kaydet
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Category model)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return View(model);

        // Veritabanındaki mevcut kaydı al
        var existing = await _db.Categories.FindAsync(id);
        if (existing == null) return NotFound();

        // Sadece değişen alanları güncelle — DateTime sorununu önler
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

    // Görünürlük toggle — sidebar'daki aç/kapat butonu
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

    // Sil
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