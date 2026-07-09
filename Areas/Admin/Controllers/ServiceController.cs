using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;

namespace Portfolio.Areas.Admin.Controllers;

public class ServiceController : AdminBaseController
{
    public ServiceController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var services = await _db.Services
            .OrderBy(s => s.SortOrder)
            .ToListAsync();
        return View(services);
    }

    public IActionResult Create() => View(new Service());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Service model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            ModelState.AddModelError("", "Başlık zorunlu.");
            return View(model);
        }

        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;
        _db.Services.Add(model);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var service = await _db.Services.FindAsync(id);
        if (service == null) return NotFound();
        return View(service);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Service model)
    {
        var existing = await _db.Services.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Title = model.Title;
        existing.Description = model.Description;
        existing.IconClass = model.IconClass;
        existing.SortOrder = model.SortOrder;
        existing.Status = model.Status;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var service = await _db.Services.FindAsync(id);
        if (service == null) return NotFound();
        _db.Services.Remove(service);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}