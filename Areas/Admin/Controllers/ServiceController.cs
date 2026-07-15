using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Services;

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
    public async Task<IActionResult> Create(
        [Bind("Id,Title,Description,IconClass,Status,SortOrder")] Service model)
    {
        AdminContentValidator.ValidateService(ModelState, model);
        if (!ModelState.IsValid)
            return View(model);

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
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Id,Title,Description,IconClass,Status,SortOrder")] Service model)
    {
        var existing = await _db.Services.FindAsync(id);
        if (existing == null) return NotFound();

        AdminContentValidator.ValidateService(ModelState, model);
        if (!ModelState.IsValid)
        {
            model.Id = id;
            return View(model);
        }

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
