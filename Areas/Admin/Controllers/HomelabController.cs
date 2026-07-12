using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Services;
using System.Text.Json;

namespace Portfolio.Areas.Admin.Controllers;

public class HomelabController : AdminBaseController
{
    private readonly ISlugService _slugService;
    private readonly IReadingTimeService _readingTime;
    private readonly IMediaService _media;
    private readonly IWebHostEnvironment _env;


    public HomelabController(AppDbContext db, ISlugService slugService,
        IReadingTimeService readingTime, IMediaService media, IWebHostEnvironment env) : base(db)
    {
        _slugService = slugService;
        _readingTime = readingTime;
        _media = media;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        var posts = await _db.HomelabPosts
            .IgnoreQueryFilters()
            .Include(h => h.Category)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync();

        return View(posts);
    }

    public IActionResult Create() => View(new HomelabPost());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(HomelabPost model,
        string? HardwareUsed, string? SoftwareUsed, List<IFormFile>? Images)
    {
        if (!ModelState.IsValid)      // ← bunu ekle
            return View(model);

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Slug == "homelab");
        if (category == null)
        {
            ModelState.AddModelError("", "Homelab kategorisi bulunamadı.");
            return View(model);
        }

        model.CategoryId = category.Id;
        model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "HomelabPosts");
        model.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(HardwareUsed))
        {
            var hw = HardwareUsed.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(h => h.Trim()).ToList();
            model.HardwareUsed = JsonSerializer.Serialize(hw);
        }

        if (!string.IsNullOrEmpty(SoftwareUsed))
        {
            var sw = SoftwareUsed.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).ToList();
            model.SoftwareUsed = JsonSerializer.Serialize(sw);
        }

        _db.HomelabPosts.Add(model);
        await _db.SaveChangesAsync();

        if (Images != null && Images.Any())
        {
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "homelab_post", model.Id);

            var firstMedia = await _db.Media
                .FirstOrDefaultAsync(m => m.EntityType == "homelab_post" && m.EntityId == model.Id);
            if (firstMedia != null)
            {
                firstMedia.IsCover = true;
                model.CoverImageUrl = firstMedia.Url;
                await _db.SaveChangesAsync();
            }
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var post = await _db.HomelabPosts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(h => h.Id == id);

        if (post == null) return NotFound();

        ViewBag.HardwareString = string.IsNullOrEmpty(post.HardwareUsed) ? ""
            : string.Join(", ", JsonSerializer.Deserialize<List<string>>(post.HardwareUsed) ?? new());

        ViewBag.SoftwareString = string.IsNullOrEmpty(post.SoftwareUsed) ? ""
            : string.Join(", ", JsonSerializer.Deserialize<List<string>>(post.SoftwareUsed) ?? new());

        ViewBag.Images = await _media.GetByEntityAsync("homelab_post", id);

        return View(post);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, HomelabPost model,
    string? HardwareUsed, string? SoftwareUsed, string? NetworkTopologyJson, List<IFormFile>? Images)
    {
        var existing = await _db.HomelabPosts.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == id);
        if (existing == null) return NotFound();

        existing.Title = model.Title;
        existing.Summary = model.Summary;
        existing.Content = model.Content;
        existing.Topic = model.Topic;
        existing.NetworkDiagramUrl = model.NetworkDiagramUrl;
        existing.Status = model.Status;
        existing.IsFeatured = model.IsFeatured;
        existing.IsMainLab = model.IsMainLab;
        existing.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        existing.UpdatedAt = DateTime.UtcNow;

        if (existing.Title != model.Title)
            existing.Slug = await _slugService.GenerateUniqueAsync(model.Title, "HomelabPosts", id);

        if (!string.IsNullOrEmpty(HardwareUsed))
            existing.HardwareUsed = JsonSerializer.Serialize(
                HardwareUsed.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(h => h.Trim()).ToList());

        if (!string.IsNullOrEmpty(SoftwareUsed))
            existing.SoftwareUsed = JsonSerializer.Serialize(
                SoftwareUsed.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList());

        // Network topology JSON'ı kaydet
        if (!string.IsNullOrEmpty(NetworkTopologyJson))
            existing.NetworkTopology = NetworkTopologyJson;

        await _db.SaveChangesAsync();

        if (Images != null && Images.Any())
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "homelab_post", id);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int mediaId, int postId)
    {
        await _media.DeleteAsync(mediaId);
        return RedirectToAction(nameof(Edit), new { id = postId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCover(int mediaId, int postId)
    {
        await _media.SetCoverAsync(mediaId, "homelab_post", postId);
        var media = await _db.Media.FindAsync(mediaId);
        var post = await _db.HomelabPosts.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == postId);
        if (post != null && media != null)
        {
            post.CoverImageUrl = media.Url;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Edit), new { id = postId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var post = await _db.HomelabPosts.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == id);
        if (post == null) return NotFound();

        post.Status = post.Status == VisibilityStatus.Public
            ? VisibilityStatus.Draft : VisibilityStatus.Public;

        if (post.Status == VisibilityStatus.Public && post.PublishedAt == null)
            post.PublishedAt = DateTime.UtcNow;

        post.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var post = await _db.HomelabPosts.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == id);
        if (post == null) return NotFound();

        var images = await _media.GetByEntityAsync("homelab_post", id);
        foreach (var img in images)
            await _media.DeleteAsync(img.Id);

        _db.HomelabPosts.Remove(post);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadIcon(IFormFile icon)
    {
        if (icon == null || icon.Length == 0)
            return Json(new { success = false, message = "Dosya seçilmedi." });

        var allowedTypes = new[] { "image/svg+xml", "image/png", "image/webp" };
        if (!allowedTypes.Contains(icon.ContentType))
            return Json(new { success = false, message = "Sadece SVG, PNG veya WebP yükleyebilirsin." });

        if (icon.Length > 500_000) // 500KB limit — ikonlar küçük olmalı
            return Json(new { success = false, message = "Dosya çok büyük (max 500KB)." });

        var extension = Path.GetExtension(icon.FileName);
        var uniqueName = $"{Guid.NewGuid()}{extension}";
        var relativePath = Path.Combine("uploads", "network-icons", uniqueName);
        var physicalPath = Path.Combine(_env.WebRootPath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        await using var stream = new FileStream(physicalPath, FileMode.Create);
        await icon.CopyToAsync(stream);

        var url = "/" + relativePath.Replace('\\', '/');
        return Json(new { success = true, url });
    }

    [HttpGet]
    public async Task<IActionResult> GetElectronicsProjects()
    {
        var projects = await _db.Projects
            .IgnoreQueryFilters()
            .Include(p => p.Category)
            .Where(p => p.Category.Slug == "electronics")
            .Select(p => new { slug = p.Slug, title = p.Title })
            .ToListAsync();

        return Json(projects);
    }
}