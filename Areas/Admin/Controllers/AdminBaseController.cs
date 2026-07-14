using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;

namespace Portfolio.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class AdminBaseController : Controller
{
    protected readonly AppDbContext _db;

    public AdminBaseController(AppDbContext db)
    {
        _db = db;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Load categories for the sidebar before each action runs.
        ViewBag.Categories = await _db.Categories
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        await next();
    }
}
