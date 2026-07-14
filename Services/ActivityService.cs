using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.ViewModels;

namespace Portfolio.Services;

public interface IActivityService
{
    Task<List<ActivityItem>> GetRecentActivityAsync(AppDbContext db, int limit);
}

public class ActivityService : IActivityService
{
    public async Task<List<ActivityItem>> GetRecentActivityAsync(AppDbContext db, int limit)
    {
        var items = new List<ActivityItem>();

        var security = await db.SecurityResearches
            .OrderByDescending(s => s.PublishedAt)
            .Take(limit)
            .Select(s => new ActivityItem
            {
                Title = s.Title,
                Summary = s.Summary,
                Url = $"/security/{s.Slug}",
                Type = "security",
                TypeLabel = "Security Research",
                ColorClass = "text-red-400 bg-red-900/20 border-red-900/50",
                Date = s.PublishedAt
            })
            .ToListAsync();

        var projects = await db.Projects
            .Include(p => p.Category)
            .OrderByDescending(p => p.PublishedAt)
            .Take(limit)
            .Select(p => new ActivityItem
            {
                Title = p.Title,
                Summary = p.Summary,
                Url = $"/{p.Category.Slug}/{p.Slug}",
                Type = p.Category.Slug,
                TypeLabel = p.Category.Slug == "electronics" ? "Electronics Project" : "Web Application",
                ColorClass = p.Category.Slug == "electronics"
                    ? "text-blue-400 bg-blue-900/20 border-blue-900/50"
                    : "text-purple-400 bg-purple-900/20 border-purple-900/50",
                Date = p.PublishedAt
            })
            .ToListAsync();

        var homelab = await db.HomelabPosts
            .OrderByDescending(h => h.PublishedAt)
            .Take(limit)
            .Select(h => new ActivityItem
            {
                Title = h.Title,
                Summary = h.Summary,
                Url = $"/homelab/{h.Slug}",
                Type = "homelab",
                TypeLabel = "Homelab",
                ColorClass = "text-teal-400 bg-teal-900/20 border-teal-900/50",
                Date = h.PublishedAt
            })
            .ToListAsync();

        var blog = await db.BlogPosts
            .OrderByDescending(b => b.PublishedAt)
            .Take(limit)
            .Select(b => new ActivityItem
            {
                Title = b.Title,
                Summary = b.Summary,
                Url = $"/blog/{b.Slug}",
                Type = "blog",
                TypeLabel = "Blog",
                ColorClass = "text-purple-400 bg-purple-900/20 border-purple-900/50",
                Date = b.PublishedAt
            })
            .ToListAsync();

        var team = await db.TeamProjects
            .OrderByDescending(t => t.PublishedAt)
            .Take(limit)
            .Select(t => new ActivityItem
            {
                Title = t.Title,
                Summary = t.Summary,
                Url = $"/team/{t.Slug}",
                Type = "team",
                TypeLabel = "Team Project",
                ColorClass = "text-amber-400 bg-amber-900/20 border-amber-900/50",
                Date = t.PublishedAt
            })
            .ToListAsync();

        items.AddRange(security);
        items.AddRange(projects);
        items.AddRange(homelab);
        items.AddRange(blog);
        items.AddRange(team);

        return items
            .Where(i => i.Date.HasValue)
            .OrderByDescending(i => i.Date)
            .Take(limit)
            .ToList();
    }
}
