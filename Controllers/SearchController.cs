using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.ViewModels;

namespace Portfolio.Controllers;

public class SearchController : BaseController
{
    public SearchController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index(string? q)
    {
        ViewBag.Query = q;

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return View(new List<SearchResultItem>());
        }

        var term = q.Trim();
        var results = new List<SearchResultItem>();

        // Güvenlik araştırmaları
        var security = await _db.SecurityResearches
            .Where(s => EF.Functions.ToTsVector("turkish", s.Title + " " + s.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("turkish", term)))
            .Select(s => new
            {
                Item = s,
                Rank = EF.Functions.ToTsVector("turkish", s.Title + " " + s.Summary)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("turkish", term))
            })
            .OrderByDescending(x => x.Rank)
            .Take(15)
            .Select(x => new SearchResultItem
            {
                Title = x.Item.Title,
                Summary = x.Item.Summary,
                Url = $"/security/{x.Item.Slug}",
                TypeLabel = "Güvenlik Araştırması",
                ColorClass = "text-red-400 bg-red-900/20 border-red-900/50",
                Date = x.Item.PublishedAt
            })
            .ToListAsync();

        // Projeler
        var projects = await _db.Projects
            .Include(p => p.Category)
            .Where(p => EF.Functions.ToTsVector("turkish", p.Title + " " + p.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("turkish", term)))
            .Select(p => new
            {
                Item = p,
                Rank = EF.Functions.ToTsVector("turkish", p.Title + " " + p.Summary)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("turkish", term))
            })
            .OrderByDescending(x => x.Rank)
            .Take(15)
            .Select(x => new SearchResultItem
            {
                Title = x.Item.Title,
                Summary = x.Item.Summary,
                Url = $"/{x.Item.Category.Slug}/{x.Item.Slug}",
                TypeLabel = x.Item.Category.Slug == "electronics" ? "Elektronik Projesi" : "Web Uygulaması",
                ColorClass = x.Item.Category.Slug == "electronics"
                    ? "text-blue-400 bg-blue-900/20 border-blue-900/50"
                    : "text-purple-400 bg-purple-900/20 border-purple-900/50",
                Date = x.Item.PublishedAt
            })
            .ToListAsync();

        // Homelab
        var homelab = await _db.HomelabPosts
            .Where(h => EF.Functions.ToTsVector("turkish", h.Title + " " + h.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("turkish", term)))
            .Select(h => new
            {
                Item = h,
                Rank = EF.Functions.ToTsVector("turkish", h.Title + " " + h.Summary)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("turkish", term))
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
            .Where(b => EF.Functions.ToTsVector("turkish", b.Title + " " + b.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("turkish", term)))
            .Select(b => new
            {
                Item = b,
                Rank = EF.Functions.ToTsVector("turkish", b.Title + " " + b.Summary)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("turkish", term))
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

        // Ekip projeleri
        var team = await _db.TeamProjects
            .Where(t => EF.Functions.ToTsVector("turkish", t.Title + " " + t.Summary)
                        .Matches(EF.Functions.PlainToTsQuery("turkish", term)))
            .Select(t => new
            {
                Item = t,
                Rank = EF.Functions.ToTsVector("turkish", t.Title + " " + t.Summary)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("turkish", term))
            })
            .OrderByDescending(x => x.Rank)
            .Take(15)
            .Select(x => new SearchResultItem
            {
                Title = x.Item.Title,
                Summary = x.Item.Summary,
                Url = $"/team/{x.Item.Slug}",
                TypeLabel = "Ekip Projesi",
                ColorClass = "text-amber-400 bg-amber-900/20 border-amber-900/50",
                Date = x.Item.PublishedAt
            })
            .ToListAsync();

        // Sayfalar
        var pages = await _db.Pages
            .Where(p => EF.Functions.ToTsVector("turkish", p.Title + " " + p.Content)
                        .Matches(EF.Functions.PlainToTsQuery("turkish", term)))
            .Select(p => new
            {
                Item = p,
                Rank = EF.Functions.ToTsVector("turkish", p.Title + " " + p.Content)
                       .RankCoverDensity(EF.Functions.PlainToTsQuery("turkish", term))
            })
            .OrderByDescending(x => x.Rank)
            .Take(10)
            .Select(x => new SearchResultItem
            {
                Title = x.Item.Title,
                Summary = "",
                Url = $"/pages/{x.Item.Slug}",
                TypeLabel = "Sayfa",
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