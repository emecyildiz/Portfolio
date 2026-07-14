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

        if (category?.IsPrivate == true)
            return NotFound();

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
        var post = await _db.HomelabPosts
            .Include(h => h.Category)
            .FirstOrDefaultAsync(h => h.Slug == slug);

        if (post == null) return NotFound();

        await _viewCount.IncrementAsync("HomelabPosts", post.Id);

        ViewBag.ContentHtml = MarkdownContentRenderer.ToHtml(post.Content);

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

        ViewBag.LinkedProjectsJson = "[]";

        // Topolojiyi tip, boyut ve URL kurallarıyla doğrulayıp script bağlamı için yeniden serialize et.
        if (NetworkTopologyJsonService.TryNormalize(
                post.NetworkTopology, out var topology, out var normalizedTopology) &&
            topology != null && normalizedTopology != null)
        {
            ViewBag.NetworkTopologyJson = normalizedTopology;
            var linkedSlugs = topology.Nodes
                .Select(node => node.LinkedProjectSlug)
                .Where(slug => !string.IsNullOrWhiteSpace(slug))
                .Distinct()
                .ToList();

            if (linkedSlugs.Any())
            {
                var linkedProjects = await _db.Projects
                    .Where(p => linkedSlugs.Contains(p.Slug))
                    .Select(p => new
                    {
                        slug = p.Slug,
                        title = p.Title,
                        coverImageUrl = p.CoverImageUrl,
                        summary = p.Summary,
                        extraData = p.ExtraData
                    })
                    .ToListAsync();

                ViewBag.LinkedProjectsJson = JsonSerializer.Serialize(linkedProjects);
            }
        }

        return View(post);
    }
}
