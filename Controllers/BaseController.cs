using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.Enums;

namespace Portfolio.Controllers;

public class BaseController : Controller
{
    protected readonly AppDbContext _db;

    public BaseController(AppDbContext db)
    {
        _db = db;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ViewBag.NavCategories = await _db.Categories
            .Where(c => c.Status == VisibilityStatus.Public && !c.IsPrivate)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        ViewBag.NavPages = await _db.Pages
        .Where(p => p.ShowInNav)
        .OrderBy(p => p.SortOrder)
        .ToListAsync();

        await next();
    }
}