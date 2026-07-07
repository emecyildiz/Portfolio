using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Services;

namespace Portfolio.Controllers;

public class BlogController : Controller
{
    private readonly AppDbContext _db;
    private readonly IViewCountService _viewCount;

    public BlogController(AppDbContext db, IViewCountService viewCount)
    {
        _db = db;
        _viewCount = viewCount;
    }

    public async Task<IActionResult> Index()
    {
        var posts = await _db.BlogPosts
            .Include(b => b.Category)
            .OrderByDescending(b => b.PublishedAt)
            .ToListAsync();

        return View(posts);
    }

    public async Task<IActionResult> Detail(string slug)
    {
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