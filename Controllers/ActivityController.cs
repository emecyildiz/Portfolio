using Microsoft.AspNetCore.Mvc;
using Portfolio.Data;
using Portfolio.Services;

namespace Portfolio.Controllers;

public class ActivityController : BaseController
{
    private readonly IActivityService _activity;

    public ActivityController(AppDbContext db, IActivityService activity) : base(db)
    {
        _activity = activity;
    }

    public async Task<IActionResult> Index()
    {
        var items = await _activity.GetRecentActivityAsync(_db, 100);
        return View(items);
    }
}