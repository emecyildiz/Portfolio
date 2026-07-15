using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Services;

namespace Portfolio.Areas.Admin.Controllers;

public class BlogController : AdminBaseController
{
    private readonly ISlugService _slugService;
    private readonly IReadingTimeService _readingTime;
    private readonly IMediaService _media;

    public BlogController(AppDbContext db, ISlugService slugService,
        IReadingTimeService readingTime, IMediaService media) : base(db)
    {
        _slugService = slugService;
        _readingTime = readingTime;
        _media = media;
    }

    public async Task<IActionResult> Index()
    {
        var posts = await _db.BlogPosts
            .IgnoreQueryFilters()
            .Include(b => b.Category)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        return View(posts);
    }

    public IActionResult Create() => View(new BlogPost());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [Bind("Id,Title,Summary,Content,IsFeatured,Status")] BlogPost model,
        List<IFormFile>? Images)
    {
        AdminContentValidator.ValidateBlog(ModelState, model, _slugService);
        await AdminContentValidator.ValidateImagesAsync(ModelState, _media, Images);

        if (!ModelState.IsValid)
            return View(model);

        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Slug == "blog");
        if (category == null)
        {
            ModelState.AddModelError("", "The blog category could not be found.");
            return View(model);
        }

        model.CategoryId = category.Id;
        model.Slug = await _slugService.GenerateUniqueAsync(model.Title, "BlogPosts");
        model.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;
        model.PublishedAt = model.Status == VisibilityStatus.Public ? DateTime.UtcNow : null;

        _db.BlogPosts.Add(model);
        await _db.SaveChangesAsync();

        if (Images != null && Images.Any())
        {
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "blog_post", model.Id);

            var firstMedia = await _db.Media
                .FirstOrDefaultAsync(m => m.EntityType == "blog_post" && m.EntityId == model.Id);
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
        var post = await _db.BlogPosts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.Id == id);

        if (post == null) return NotFound();

        ViewBag.Images = await _media.GetByEntityAsync("blog_post", id);
        return View(post);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        [Bind("Id,Title,Summary,Content,IsFeatured,Status")] BlogPost model,
        List<IFormFile>? Images)
    {
        var existing = await _db.BlogPosts.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == id);
        if (existing == null) return NotFound();

        AdminContentValidator.ValidateBlog(ModelState, model, _slugService);
        await AdminContentValidator.ValidateImagesAsync(ModelState, _media, Images);
        if (!ModelState.IsValid)
        {
            model.Id = id;
            ViewBag.Images = await _media.GetByEntityAsync("blog_post", id);
            return View(model);
        }

        if (existing.Title != model.Title)
            existing.Slug = await _slugService.GenerateUniqueAsync(model.Title, "BlogPosts", id);

        existing.Title = model.Title;
        existing.Summary = model.Summary;
        existing.Content = model.Content;
        existing.Status = model.Status;
        if (existing.Status == VisibilityStatus.Public && existing.PublishedAt == null)
            existing.PublishedAt = DateTime.UtcNow;

        existing.IsFeatured = model.IsFeatured;
        existing.ReadingTimeMinutes = _readingTime.Calculate(model.Content);
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        if (Images != null && Images.Any())
            foreach (var file in Images.Where(f => f.Length > 0))
                await _media.SaveAsync(file, "blog_post", id);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int mediaId, int postId)
    {
        if (!await _media.DeleteAsync(mediaId, "blog_post", postId))
            return NotFound();

        return RedirectToAction(nameof(Edit), new { id = postId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCover(int mediaId, int postId)
    {
        if (!await _media.SetCoverAsync(mediaId, "blog_post", postId))
            return NotFound();

        return RedirectToAction(nameof(Edit), new { id = postId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id)
    {
        var post = await _db.BlogPosts.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == id);
        if (post == null) return NotFound();

        if (post.Status != VisibilityStatus.Public &&
            !AdminPublishValidator.CanPublishBlog(ModelState, post, _slugService))
        {
            TempData["Error"] = "This post cannot be published yet. Open Edit and correct the highlighted fields.";
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
        var post = await _db.BlogPosts.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == id);
        if (post == null) return NotFound();

        var images = await _media.GetByEntityAsync("blog_post", id);
        foreach (var img in images)
            await _media.DeleteAsync(img.Id);

        _db.BlogPosts.Remove(post);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
