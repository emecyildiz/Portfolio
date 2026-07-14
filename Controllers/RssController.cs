using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using System.Text;
using System.Xml.Linq;

namespace Portfolio.Controllers;

public class RssController : Controller
{
    private readonly AppDbContext _db;

    public RssController(AppDbContext db)
    {
        _db = db;
    }

    [Route("blog/rss.xml")]
    public async Task<IActionResult> BlogFeed()
    {
        var posts = await _db.BlogPosts
            .OrderByDescending(b => b.PublishedAt)
            .Take(20)
            .ToListAsync();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XElement("channel",
                new XElement("title", "Blog — Portfolio"),
                new XElement("link", $"{baseUrl}/blog"),
                new XElement("description", "Daily work and technical notes"),
                new XElement("language", "en-US"),
                posts.Select(p => new XElement("item",
                    new XElement("title", p.Title),
                    new XElement("link", $"{baseUrl}/blog/{p.Slug}"),
                    new XElement("guid", $"{baseUrl}/blog/{p.Slug}"),
                    new XElement("description", p.Summary),
                    new XElement("pubDate", (p.PublishedAt ?? p.CreatedAt).ToString("R"))
                ))
            )
        );

        var xml = new XDocument(rss).ToString();
        return Content(xml, "application/rss+xml", Encoding.UTF8);
    }
}
