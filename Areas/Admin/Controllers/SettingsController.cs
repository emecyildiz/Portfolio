using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;

namespace Portfolio.Areas.Admin.Controllers;

public class SettingsController : AdminBaseController
{
    private readonly IWebHostEnvironment _env;

    public SettingsController(AppDbContext db, IWebHostEnvironment env) : base(db)
    {
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        var settings = await _db.SiteSettings.FirstOrDefaultAsync();
        settings ??= new SiteSettings();
        return View(settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadCv(IFormFile cvFile)
    {
        if (cvFile == null || cvFile.Length == 0)
        {
            TempData["Error"] = "Dosya seçilmedi.";
            return RedirectToAction(nameof(Index));
        }

        if (Path.GetExtension(cvFile.FileName).ToLower() != ".pdf")
        {
            TempData["Error"] = "Sadece PDF dosyası yükleyebilirsin.";
            return RedirectToAction(nameof(Index));
        }

        // Sabit isimle kaydet — her yüklemede üzerine yazılır
        var relativePath = Path.Combine("uploads", "cv", "cv.pdf");
        var physicalPath = Path.Combine(_env.WebRootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        await using var stream = new FileStream(physicalPath, FileMode.Create);
        await cvFile.CopyToAsync(stream);

        var settings = await _db.SiteSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new SiteSettings();
            _db.SiteSettings.Add(settings);
        }

        settings.CvFileUrl = "/" + relativePath.Replace('\\', '/');
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "CV yüklendi.";
        return RedirectToAction(nameof(Index));
    }
}