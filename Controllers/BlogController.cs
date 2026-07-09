using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.Enums;
using Portfolio.Services;

namespace Portfolio.Controllers;

public class BlogController : BaseController
{
    private readonly IViewCountService _viewCount;

    public BlogController(AppDbContext db, IViewCountService viewCount) : base (db)
    {
        _viewCount = viewCount;
    }

    public async Task<IActionResult> Index()
    {
        var category = await _db.Categories
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Slug == "blog");

        if (category == null || category.Status != VisibilityStatus.Public)
            return View("CategoryUnavailable");

        var posts = await _db.BlogPosts
            .Include(b => b.Category)
            .OrderByDescending(b => b.PublishedAt)
            .ToListAsync();

        return View(posts);
    }

    public async Task<IActionResult> Detail(string slug)
    {
        var category = await _db.Categories
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Slug == "blog");

        if (category == null || category.Status != VisibilityStatus.Public)
            return View("CategoryUnavailable");

        var post = await _db.BlogPosts
            .Include(b => b.Category)
            .FirstOrDefaultAsync(b => b.Slug == slug);

        if (post == null) return NotFound();

        await _viewCount.IncrementAsync("BlogPosts", post.Id);

        ViewBag.ContentHtml = Markdown.ToHtml(post.Content ?? "",
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());

        ViewBag.Related = await _db.BlogPosts
            .Where(b => b.Id != post.Id)
            .OrderByDescending(b => b.PublishedAt)
            .Take(3)
            .ToListAsync();

        return View(post);
    }
}