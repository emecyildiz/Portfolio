using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using System.Text;
using System.Xml.Linq;

namespace Portfolio.Controllers;

public class SitemapController : Controller
{
    private readonly AppDbContext _db;

    public SitemapController(AppDbContext db)
    {
        _db = db;
    }

    [Route("sitemap.xml")]
    public async Task<IActionResult> Index()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        // Global query filters ensure that only public content is included.
        var security = await _db.SecurityResearches
            .AsNoTracking()
            .OrderBy(s => s.Slug)
            .ToListAsync();

        var projects = await _db.Projects
            .AsNoTracking()
            .Include(p => p.Category)
            .OrderBy(p => p.Category.Slug)
            .ThenBy(p => p.Slug)
            .ToListAsync();

        var homelab = await _db.HomelabPosts
            .AsNoTracking()
            .OrderBy(h => h.Slug)
            .ToListAsync();

        var blog = await _db.BlogPosts
            .AsNoTracking()
            .OrderBy(b => b.Slug)
            .ToListAsync();

        var team = await _db.TeamProjects
            .AsNoTracking()
            .OrderBy(t => t.Slug)
            .ToListAsync();

        var pages = await _db.Pages
            .AsNoTracking()
            .OrderBy(p => p.Slug)
            .ToListAsync();

        var urls = new List<(string loc, DateTime? lastmod, string priority)>
        {
            // lastmod is optional. Omit it for code-backed static pages rather
            // than claiming that they change every time the sitemap is read.
            (baseUrl, null, "1.0"),
            ($"{baseUrl}/hire", null, "0.9")
        };

        AddSectionIfPopulated(urls, baseUrl, "security", security.Select(s => s.UpdatedAt), "0.8");
        AddSectionIfPopulated(
            urls,
            baseUrl,
            "electronics",
            projects.Where(p => p.Category.Slug == "electronics").Select(p => p.UpdatedAt),
            "0.8");
        AddSectionIfPopulated(
            urls,
            baseUrl,
            "webapps",
            projects.Where(p => p.Category.Slug == "webapps").Select(p => p.UpdatedAt),
            "0.8");
        AddSectionIfPopulated(urls, baseUrl, "homelab", homelab.Select(h => h.UpdatedAt), "0.8");
        AddSectionIfPopulated(urls, baseUrl, "blog", blog.Select(b => b.UpdatedAt), "0.7");
        AddSectionIfPopulated(urls, baseUrl, "team", team.Select(t => t.UpdatedAt), "0.7");

        var activityDates = security.Select(s => s.UpdatedAt)
            .Concat(projects.Select(p => p.UpdatedAt))
            .Concat(homelab.Select(h => h.UpdatedAt))
            .Concat(blog.Select(b => b.UpdatedAt))
            .Concat(team.Select(t => t.UpdatedAt));
        AddSectionIfPopulated(urls, baseUrl, "activity", activityDates, "0.5");

        urls.AddRange(security.Select(s =>
            ($"{baseUrl}/security/{s.Slug}", (DateTime?)s.UpdatedAt, "0.6")));
        urls.AddRange(projects.Select(p =>
            ($"{baseUrl}/{p.Category.Slug}/{p.Slug}", (DateTime?)p.UpdatedAt, "0.6")));
        urls.AddRange(homelab.Select(h =>
            ($"{baseUrl}/homelab/{h.Slug}", (DateTime?)h.UpdatedAt, "0.6")));
        urls.AddRange(blog.Select(b =>
            ($"{baseUrl}/blog/{b.Slug}", (DateTime?)b.UpdatedAt, "0.5")));
        urls.AddRange(team.Select(t =>
            ($"{baseUrl}/team/{t.Slug}", (DateTime?)t.UpdatedAt, "0.5")));
        urls.AddRange(pages.Select(p =>
            ($"{baseUrl}/pages/{p.Slug}", (DateTime?)p.UpdatedAt, "0.4")));

        var urlset = new XElement(ns + "urlset",
            urls.Select(u => new XElement(ns + "url",
                new XElement(ns + "loc", u.loc),
                u.lastmod.HasValue
                    ? new XElement(ns + "lastmod", u.lastmod.Value.ToString("yyyy-MM-dd"))
                    : null,
                new XElement(ns + "priority", u.priority)
            ))
        );

        var xml = new XDocument(urlset).ToString();
        return Content(xml, "application/xml", Encoding.UTF8);
    }

    private static void AddSectionIfPopulated(
        ICollection<(string loc, DateTime? lastmod, string priority)> urls,
        string baseUrl,
        string path,
        IEnumerable<DateTime> contentDates,
        string priority)
    {
        var dates = contentDates as IReadOnlyCollection<DateTime> ?? contentDates.ToArray();
        if (dates.Count == 0)
            return;

        urls.Add(($"{baseUrl}/{path}", dates.Max(), priority));
    }
}
