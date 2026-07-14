using Markdig;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Services;

namespace Portfolio.Controllers;

public class PageController : BaseController
{
    public PageController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Detail(string slug)
    {
        var page = await _db.Pages.FirstOrDefaultAsync(p => p.Slug == slug);
        if (page == null) return NotFound();

        ViewBag.ContentHtml = MarkdownContentRenderer.ToHtml(page.Content);

        return View(page);
    }
}
