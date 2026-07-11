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

        var urls = new List<(string loc, DateTime lastmod, string priority)>
        {
            (baseUrl, DateTime.UtcNow, "1.0"),
            ($"{baseUrl}/security", DateTime.UtcNow, "0.8"),
            ($"{baseUrl}/electronics", DateTime.UtcNow, "0.8"),
            ($"{baseUrl}/webapps", DateTime.UtcNow, "0.8"),
            ($"{baseUrl}/homelab", DateTime.UtcNow, "0.8"),
            ($"{baseUrl}/blog", DateTime.UtcNow, "0.7"),
            ($"{baseUrl}/team", DateTime.UtcNow, "0.7"),
            ($"{baseUrl}/hire", DateTime.UtcNow, "0.9"),
            ($"{baseUrl}/activity", DateTime.UtcNow, "0.5"),
        };

        // Sadece public durumdaki içerikler eklenir — global query filter zaten bunu sağlıyor
        var security = await _db.SecurityResearches.ToListAsync();
        urls.AddRange(security.Select(s => ($"{baseUrl}/security/{s.Slug}", s.UpdatedAt, "0.6")));

        var projects = await _db.Projects.Include(p => p.Category).ToListAsync();
        urls.AddRange(projects.Select(p => ($"{baseUrl}/{p.Category.Slug}/{p.Slug}", p.UpdatedAt, "0.6")));

        var homelab = await _db.HomelabPosts.ToListAsync();
        urls.AddRange(homelab.Select(h => ($"{baseUrl}/homelab/{h.Slug}", h.UpdatedAt, "0.6")));

        var blog = await _db.BlogPosts.ToListAsync();
        urls.AddRange(blog.Select(b => ($"{baseUrl}/blog/{b.Slug}", b.UpdatedAt, "0.5")));

        var team = await _db.TeamProjects.ToListAsync();
        urls.AddRange(team.Select(t => ($"{baseUrl}/team/{t.Slug}", t.UpdatedAt, "0.5")));

        var pages = await _db.Pages.ToListAsync();
        urls.AddRange(pages.Select(p => ($"{baseUrl}/pages/{p.Slug}", p.UpdatedAt, "0.4")));

        var urlset = new XElement(ns + "urlset",
            urls.Select(u => new XElement(ns + "url",
                new XElement(ns + "loc", u.loc),
                new XElement(ns + "lastmod", u.lastmod.ToString("yyyy-MM-dd")),
                new XElement(ns + "priority", u.priority)
            ))
        );

        var xml = new XDocument(urlset).ToString();
        return Content(xml, "application/xml", Encoding.UTF8);
    }
}