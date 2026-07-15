using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Portfolio.Data;
using Portfolio.Models.ViewModels;

namespace Portfolio.Controllers;

public class SearchController : BaseController
{
    public SearchController(AppDbContext db) : base(db) { }

    [EnableRateLimiting("SearchLimit")]
    public async Task<IActionResult> Index(string? q)
    {
        var term = q?.Trim();
        ViewBag.Query = term;

        if (string.IsNullOrWhiteSpace(term) || term.Length < 2)
        {
            return View(new List<SearchResultItem>());
        }

        if (term.Length > 100)
        {
            ViewBag.SearchError = "Search queries cannot exceed 100 characters.";
            return View(new List<SearchResultItem>());
        }

        var results = new List<SearchResultItem>();

        // Security research
        var security = await _db.SecurityResearches
            .Where(s => EF.Functions.ToTsVector("english", s.Title + " " + s.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("english", term)))
            .Select(s => new
            {
                Item = s,
                Rank = EF.Functions.ToTsVector("english", s.Title + " " + s.Summary)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("english", term))
            })
            .OrderByDescending(x => x.Rank)
            .Take(15)
            .Select(x => new SearchResultItem
            {
                Title = x.Item.Title,
                Summary = x.Item.Summary,
                Url = $"/security/{x.Item.Slug}",
                TypeLabel = "Security Research",
                ColorClass = "text-red-400 bg-red-900/20 border-red-900/50",
                Date = x.Item.PublishedAt
            })
            .ToListAsync();

        // Projects
        var projects = await _db.Projects
            .Include(p => p.Category)
            .Where(p => EF.Functions.ToTsVector("english", p.Title + " " + p.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("english", term)))
            .Select(p => new
            {
                Item = p,
                Rank = EF.Functions.ToTsVector("english", p.Title + " " + p.Summary)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("english", term))
            })
            .OrderByDescending(x => x.Rank)
            .Take(15)
            .Select(x => new SearchResultItem
            {
                Title = x.Item.Title,
                Summary = x.Item.Summary,
                Url = $"/{x.Item.Category.Slug}/{x.Item.Slug}",
                TypeLabel = x.Item.Category.Slug == "electronics" ? "Electronics Project" : "Web Application",
                ColorClass = x.Item.Category.Slug == "electronics"
                    ? "text-blue-400 bg-blue-900/20 border-blue-900/50"
                    : "text-purple-400 bg-purple-900/20 border-purple-900/50",
                Date = x.Item.PublishedAt
            })
            .ToListAsync();

        // Homelab
        var homelab = await _db.HomelabPosts
            .Where(h => EF.Functions.ToTsVector("english", h.Title + " " + h.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("english", term)))
            .Select(h => new
            {
                Item = h,
                Rank = EF.Functions.ToTsVector("english", h.Title + " " + h.Summary)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("english", term))
            })
            .OrderByDescending(x => x.Rank)
            .Take(15)
            .Select(x => new SearchResultItem
            {
                Title = x.Item.Title,
                Summary = x.Item.Summary,
                Url = $"/homelab/{x.Item.Slug}",
                TypeLabel = "Homelab",
                ColorClass = "text-teal-400 bg-teal-900/20 border-teal-900/50",
                Date = x.Item.PublishedAt
            })
            .ToListAsync();

        // Blog
        var blog = await _db.BlogPosts
            .Where(b => EF.Functions.ToTsVector("english", b.Title + " " + b.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("english", term)))
            .Select(b => new
            {
                Item = b,
                Rank = EF.Functions.ToTsVector("english", b.Title + " " + b.Summary)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("english", term))
            })
            .OrderByDescending(x => x.Rank)
            .Take(15)
            .Select(x => new SearchResultItem
            {
                Title = x.Item.Title,
                Summary = x.Item.Summary,
                Url = $"/blog/{x.Item.Slug}",
                TypeLabel = "Blog",
                ColorClass = "text-purple-400 bg-purple-900/20 border-purple-900/50",
                Date = x.Item.PublishedAt
            })
            .ToListAsync();

        // Team projects
        var team = await _db.TeamProjects
            .Where(t => EF.Functions.ToTsVector("english", t.Title + " " + t.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("english", term)))
            .Select(t => new
            {
                Item = t,
                Rank = EF.Functions.ToTsVector("english", t.Title + " " + t.Summary)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("english", term))
            })
            .OrderByDescending(x => x.Rank)
            .Take(15)
            .Select(x => new SearchResultItem
            {
                Title = x.Item.Title,
                Summary = x.Item.Summary,
                Url = $"/team/{x.Item.Slug}",
                TypeLabel = "Team Project",
                ColorClass = "text-amber-400 bg-amber-900/20 border-amber-900/50",
                Date = x.Item.PublishedAt
            })
            .ToListAsync();

        // Pages
        var pages = await _db.Pages
            .Where(p => EF.Functions.ToTsVector("english", p.Title + " " + p.Content)
                        .Matches(EF.Functions.PlainToTsQuery("english", term)))
            .Select(p => new
            {
                Item = p,
                Rank = EF.Functions.ToTsVector("english", p.Title + " " + p.Content)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("english", term))
            })
            .OrderByDescending(x => x.Rank)
            .Take(10)
            .Select(x => new SearchResultItem
            {
                Title = x.Item.Title,
                Summary = "",
                Url = $"/pages/{x.Item.Slug}",
                TypeLabel = "Page",
                ColorClass = "text-gray-400 bg-gray-800 border-gray-700",
                Date = x.Item.CreatedAt
            })
            .ToListAsync();

        results.AddRange(security);
        results.AddRange(projects);
        results.AddRange(homelab);
        results.AddRange(blog);
        results.AddRange(team);
        results.AddRange(pages);

        return View(results.OrderByDescending(r => r.Date).ToList());
    }
}
