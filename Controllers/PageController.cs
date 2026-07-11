using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

namespace Portfolio.Controllers;

public class PageController : BaseController
{
    public PageController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Detail(string slug)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Slug == slug);
        if (page == null) return NotFound();

        ViewBag.ContentHtml = Markdown.ToHtml(page.Content ?? "",
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());

        return View(page);
    }
}