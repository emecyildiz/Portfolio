using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models.ViewModels;

namespace Portfolio.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class DashboardController : AdminBaseController
{

    public DashboardController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var model = new DashboardViewModel
        {
            CategoryCount = await _db.Categories.CountAsync(),
            ProjectCount = await _db.Projects.IgnoreQueryFilters().CountAsync(),
            SecurityCount = await _db.SecurityResearches.IgnoreQueryFilters().CountAsync(),
            HomelabCount = await _db.HomelabPosts.IgnoreQueryFilters().CountAsync(),
            BlogCount = await _db.BlogPosts.IgnoreQueryFilters().CountAsync(),
            TeamCount = await _db.TeamProjects.IgnoreQueryFilters().CountAsync(),
            UnreadMessages = await _db.ContactMessages.CountAsync(m => !m.IsRead),
            RecentCategories = await _db.Categories.OrderBy(c => c.SortOrder).ToListAsync(),
        };

        return View(model);
    }
}