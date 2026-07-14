using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Services;

namespace Portfolio.Areas.Admin.Controllers;

public class SettingsController : AdminBaseController
{
    private const long MaxCvFileSize = 10_485_760;
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

        if (cvFile.Length > MaxCvFileSize)
        {
            TempData["Error"] = "CV dosyası en fazla 10 MB olabilir.";
            return RedirectToAction(nameof(Index));
        }

        if (!await UploadFileValidator.ValidatePdfAsync(cvFile))
        {
            TempData["Error"] = "Dosya uzantısı, içerik türü ve içeriği geçerli bir PDF olmalı.";
            return RedirectToAction(nameof(Index));
        }

        // Sabit isimle kaydet — her yüklemede üzerine yazılır
        var relativePath = Path.Combine("uploads", "cv", "cv.pdf");
        var physicalPath = Path.Combine(_env.WebRootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        var temporaryPath = $"{physicalPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await cvFile.CopyToAsync(stream);

            System.IO.File.Move(temporaryPath, physicalPath, true);
        }
        finally
        {
            if (System.IO.File.Exists(temporaryPath))
                System.IO.File.Delete(temporaryPath);
        }

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
