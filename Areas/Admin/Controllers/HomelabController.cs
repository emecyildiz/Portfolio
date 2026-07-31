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
    public async Task<IActionResult> Create(
        [Bind("Id,Title,Summary,Content,Topic,NetworkDiagramUrl,KnowledgeUrl,IsFeatured,IsMainLab,Status")] HomelabPost model,
        string? HardwareUsed, string? SoftwareUsed, List<IFormFile>? Images)
    {
        ViewBag.HardwareString = HardwareUsed ?? string.Empty;
        ViewBag.SoftwareString = SoftwareUsed ?? string.Empty;

        AdminContentValidator.ValidateHomelab(
            ModelState, model, _slugService, HardwareUsed, SoftwareUsed);
        await AdminContentValidator.ValidateImagesAsync(ModelState, _media, Images);

        if (!ModelState.IsValid)
            return View(model);

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Slug == "homelab");
        if (category == null)
        {
            ModelState.AddModelError("", "The homelab category could not be found.");
            return View(model);
        }

        model.CategoryId = category.Id;
        model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "HomelabPosts");
        model.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;
        model.PublishedAt = model.Status == VisibilityStatus.Public ? DateTime.UtcNow : null;

        var hardware = HardwareUsed?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var software = SoftwareUsed?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        model.HardwareUsed = hardware is { Count: > 0 } ? JsonSerializer.Serialize(hardware) : null;
        model.SoftwareUsed = software is { Count: > 0 } ? JsonSerializer.Serialize(software) : null;

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
        NetworkTopologyJsonService.TryNormalize(
            post.NetworkTopology, out _, out var normalizedTopology);
        ViewBag.NetworkTopologyJson = normalizedTopology;

        return View(post);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
    int id,
    [Bind("Id,Title,Summary,Content,Topic,NetworkDiagramUrl,KnowledgeUrl,IsFeatured,IsMainLab,Status")] HomelabPost model,
    string? HardwareUsed, string? SoftwareUsed, string? NetworkTopologyJson, List<IFormFile>? Images)
    {
        var existing = await _db.HomelabPosts.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == id);
        if (existing == null) return NotFound();

        AdminContentValidator.ValidateHomelab(
            ModelState, model, _slugService, HardwareUsed, SoftwareUsed);
        await AdminContentValidator.ValidateImagesAsync(ModelState, _media, Images);

        var topologyIsValid = NetworkTopologyJsonService.TryNormalize(
            NetworkTopologyJson, out _, out var normalizedTopology, out var topologyValidationError);
        if (!topologyIsValid)
        {
            ModelState.AddModelError(
                string.Empty,
                topologyValidationError ?? "The network topology is invalid or exceeds the allowed limits.");
        }

        if (!ModelState.IsValid)
        {
            model.Id = id;
            ViewBag.HardwareString = HardwareUsed ?? string.Empty;
            ViewBag.SoftwareString = SoftwareUsed ?? string.Empty;
            ViewBag.Images = await _media.GetByEntityAsync("homelab_post", id);
            var editableTopology = normalizedTopology;
            if (!topologyIsValid &&
                !NetworkTopologyJsonService.TryPrepareForEditing(NetworkTopologyJson, out editableTopology))
            {
                editableTopology = existing.NetworkTopology;
            }

            ViewBag.NetworkTopologyJson = editableTopology;
            ViewBag.TopologyValidationError = topologyValidationError;
            return View(model);
        }

        if (existing.Title != model.Title)
            existing.Slug = await _slugService.GenerateUniqueAsync(model.Title, "HomelabPosts", id);

        existing.Title = model.Title;
        existing.Summary = model.Summary;
        existing.Content = model.Content;
        existing.Topic = model.Topic;
        existing.NetworkDiagramUrl = model.NetworkDiagramUrl;
        existing.KnowledgeUrl = model.KnowledgeUrl;
        existing.Status = model.Status;
        if (existing.Status == VisibilityStatus.Public && existing.PublishedAt == null)
            existing.PublishedAt = DateTime.UtcNow;

        existing.IsFeatured = model.IsFeatured;
        existing.IsMainLab = model.IsMainLab;
        existing.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        existing.UpdatedAt = DateTime.UtcNow;

        var hardware = HardwareUsed?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var software = SoftwareUsed?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        existing.HardwareUsed = hardware is { Count: > 0 } ? JsonSerializer.Serialize(hardware) : null;
        existing.SoftwareUsed = software is { Count: > 0 } ? JsonSerializer.Serialize(software) : null;

        // Save the network topology JSON.
        if (!string.IsNullOrWhiteSpace(NetworkTopologyJson))
            existing.NetworkTopology = normalizedTopology;

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
        if (!await _media.DeleteAsync(mediaId, "homelab_post", postId))
            return NotFound();

        return RedirectToAction(nameof(Edit), new { id = postId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCover(int mediaId, int postId)
    {
        if (!await _media.SetCoverAsync(mediaId, "homelab_post", postId))
            return NotFound();

        return RedirectToAction(nameof(Edit), new { id = postId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var post = await _db.HomelabPosts.IgnoreQueryFilters().FirstOrDefaultAsync(h => h.Id == id);
        if (post == null) return NotFound();

        if (post.Status != VisibilityStatus.Public &&
            !AdminPublishValidator.CanPublishHomelab(ModelState, post, _slugService))
        {
            TempData["Error"] = "This homelab post cannot be published yet. Open Edit and correct the highlighted fields.";
            return RedirectToAction(nameof(Index));
        }

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
            return Json(new { success = false, message = "No file was selected." });

        if (icon.Length > 500_000) // Keep icons small with a 500 KB limit.
            return Json(new { success = false, message = "The file is too large (maximum 500 KB)." });

        // Accept raster icons only because user-supplied SVG can execute scripts on the same origin.
        var validatedUpload = await UploadFileValidator.ValidateImageAsync(icon, ".png", ".webp");
        if (validatedUpload == null)
            return Json(new { success = false, message = "The file must be a valid PNG or WebP icon." });

        var uniqueName = $"{Guid.NewGuid()}{validatedUpload.Extension}";
        var relativePath = Path.Combine("uploads", "network-icons", uniqueName);
        var physicalPath = Path.Combine(_env.WebRootPath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        await using var stream = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await icon.CopyToAsync(stream);

        var url = "/" + relativePath.Replace('\\', '/');
        return Json(new { success = true, url });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadDeviceImage(IFormFile image)
    {
        if (image == null || image.Length == 0)
            return Json(new { success = false, message = "No file was selected." });

        if (image.Length > 5_000_000)
            return Json(new { success = false, message = "The file is too large (maximum 5 MB)." });

        var validatedUpload = await UploadFileValidator.ValidateImageAsync(
            image, ".jpg", ".jpeg", ".png", ".webp");
        if (validatedUpload == null)
        {
            return Json(new
            {
                success = false,
                message = "The file must be a valid JPEG, PNG or WebP image."
            });
        }

        var uniqueName = $"{Guid.NewGuid()}{validatedUpload.Extension}";
        var relativePath = Path.Combine("uploads", "network-devices", uniqueName);
        var physicalPath = Path.Combine(_env.WebRootPath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);

        await using var stream = new FileStream(
            physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await image.CopyToAsync(stream);

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

    [HttpGet]
    public IActionResult GetIconLibrary()
    {
        var iconFolder = Path.Combine(_env.WebRootPath, "icons", "network");

        if (!Directory.Exists(iconFolder))
            return Json(new List<object>());

        var icons = Directory.GetFiles(iconFolder)
            .Where(f => new[] { ".svg", ".png", ".webp" }.Contains(Path.GetExtension(f).ToLower()))
            .Select(f => new
            {
                name = Path.GetFileNameWithoutExtension(f),
                url = "/icons/network/" + Path.GetFileName(f)
            })
            .OrderBy(i => i.name)
            .ToList();

        return Json(icons);
    }
}
