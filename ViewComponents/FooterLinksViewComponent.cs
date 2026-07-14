using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.ExtraData;
using Portfolio.Services;

namespace Portfolio.ViewComponents;

public sealed class FooterLinksViewComponent : ViewComponent
{
    private readonly AppDbContext _db;
    private readonly ILogger<FooterLinksViewComponent> _logger;

    public FooterLinksViewComponent(
        AppDbContext db,
        ILogger<FooterLinksViewComponent> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        try
        {
            var linksJson = await _db.SiteSettings
                .AsNoTracking()
                .OrderBy(settings => settings.Id)
                .Select(settings => settings.FooterLinksJson)
                .FirstOrDefaultAsync();

            if (!SiteLinksJsonService.TryNormalize(linksJson, out var links, out _))
                links = [];

            return View(links);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Footer links could not be loaded.");
            return View(Array.Empty<SiteLink>());
        }
    }
}
