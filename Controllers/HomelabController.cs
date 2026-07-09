using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.Enums;
using Portfolio.Services;
using System.Text.Json;

namespace Portfolio.Controllers;

public class HomelabController : BaseController
{
    private readonly IViewCountService _viewCount;

    public HomelabController(AppDbContext db, IViewCountService viewCount) : base(db)
    {
        _viewCount = viewCount;
    }

    public async Task<IActionResult> Index(string? topic)
    {
        var category = await _db.Categories
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Slug == "homelab");

        if (category == null || category.Status != VisibilityStatus.Public)
            return View("CategoryUnavailable");

        var query = _db.HomelabPosts
            .Include(h => h.Category)
            .AsQueryable();

        if (!string.IsNullOrEmpty(topic) && Enum.TryParse<HomelabTopic>(topic, out var homelabTopic))
            query = query.Where(h => h.Topic == homelabTopic);

        var posts = await query
            .OrderByDescending(h => h.PublishedAt)
            .ToListAsync();

        ViewBag.CurrentTopic = topic;
        return View(posts);
    }

    public async Task<IActionResult> Detail(string slug)
    {
        var category = await _db.Categories
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Slug == "homelab");

        if (category == null || category.Status != VisibilityStatus.Public)
            return View("CategoryUnavailable");

        var post = await _db.HomelabPosts
            .Include(h => h.Category)
            .FirstOrDefaultAsync(h => h.Slug == slug);

        if (post == null) return NotFound();

        await _viewCount.IncrementAsync("HomelabPosts", post.Id);

        ViewBag.ContentHtml = Markdown.ToHtml(post.Content ?? "",
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());

        ViewBag.Hardware = string.IsNullOrEmpty(post.HardwareUsed) ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(post.HardwareUsed) ?? new();

        ViewBag.Software = string.IsNullOrEmpty(post.SoftwareUsed) ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(post.SoftwareUsed) ?? new();

        ViewBag.Images = await _db.Media
            .Where(m => m.EntityType == "homelab_post" && m.EntityId == post.Id)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();

        ViewBag.Related = await _db.HomelabPosts
            .Where(h => h.Topic == post.Topic && h.Id != post.Id)
            .OrderByDescending(h => h.PublishedAt)
            .Take(3)
            .ToListAsync();

        return View(post);
    }
}