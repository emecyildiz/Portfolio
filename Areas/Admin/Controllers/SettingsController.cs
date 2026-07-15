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
        var settings = await _db.SiteSettings.OrderBy(item => item.Id).FirstOrDefaultAsync();
        settings ??= new SiteSettings();
        SiteLinksJsonService.TryNormalize(
            settings.FooterLinksJson, out _, out var normalizedLinks);
        ViewBag.FooterLinksJson = normalizedLinks;
        return View(settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveFooterLinks(string? footerLinksJson)
    {
        if (!SiteLinksJsonService.TryNormalize(
                footerLinksJson, out _, out var normalizedLinks))
        {
            TempData["Error"] = "The links are invalid. Only https:// and mailto: addresses are allowed.";
            return RedirectToAction(nameof(Index));
        }

        var settings = await _db.SiteSettings.OrderBy(item => item.Id).FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new SiteSettings();
            _db.SiteSettings.Add(settings);
        }

        settings.FooterLinksJson = normalizedLinks;
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Footer links were saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadCv(IFormFile cvFile)
    {
        if (cvFile == null || cvFile.Length == 0)
        {
            TempData["Error"] = "No file was selected.";
            return RedirectToAction(nameof(Index));
        }

        if (cvFile.Length > MaxCvFileSize)
        {
            TempData["Error"] = "The CV file cannot exceed 10 MB.";
            return RedirectToAction(nameof(Index));
        }

        if (!await UploadFileValidator.ValidatePdfAsync(cvFile))
        {
            TempData["Error"] = "The file extension, content type, and contents must represent a valid PDF.";
            return RedirectToAction(nameof(Index));
        }

        // Save with a fixed name so each upload replaces the previous CV.
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

        var settings = await _db.SiteSettings.OrderBy(item => item.Id).FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new SiteSettings();
            _db.SiteSettings.Add(settings);
        }

        settings.CvFileUrl = "/" + relativePath.Replace('\\', '/');
        settings.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        TempData["Success"] = "The CV was uploaded.";
        return RedirectToAction(nameof(Index));
    }
}
