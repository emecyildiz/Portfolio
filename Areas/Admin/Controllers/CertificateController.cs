using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;

namespace Portfolio.Areas.Admin.Controllers;

public class CertificateController : AdminBaseController
{
    public CertificateController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var certs = await _db.Certificates
            .IgnoreQueryFilters()
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
        return View(certs);
    }

    public IActionResult Create() => View(new Certificate());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Certificate model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            TempData["Error"] = "Başlık zorunlu.";
            return View(model);
        }

        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;
        _db.Certificates.Add(model);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Sertifika eklendi.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var cert = await _db.Certificates.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (cert == null) return NotFound();
        return View(cert);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Certificate model)
    {
        var existing = await _db.Certificates.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (existing == null) return NotFound();

        existing.Title = model.Title;
        existing.Issuer = model.Issuer;
        existing.CredentialId = model.CredentialId;
        existing.CredentialUrl = model.CredentialUrl;
        existing.ImageUrl = model.ImageUrl;
        existing.IssuedDate = model.IssuedDate;
        existing.ExpiryDate = model.ExpiryDate;
        existing.Status = model.Status;
        existing.SortOrder = model.SortOrder;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["Success"] = "Sertifika güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var cert = await _db.Certificates.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (cert == null) return NotFound();
        _db.Certificates.Remove(cert);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}