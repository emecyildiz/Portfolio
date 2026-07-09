using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Services;

namespace Portfolio.Controllers;

public class HireController : BaseController
{

    public HireController(AppDbContext db) : base(db)
    {
    }

    public async Task<IActionResult> Index()
    {

        var services = await _db.Services
            .Where(s => s.Status == VisibilityStatus.Public)
            .Include(s => s.References)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        ViewBag.Services = services;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Contact(ContactMessage model, int? serviceId)
    {

        if (string.IsNullOrWhiteSpace(model.Name) ||
            string.IsNullOrWhiteSpace(model.Email) ||
            string.IsNullOrWhiteSpace(model.Message))
        {
            TempData["Error"] = "Ad, e-posta ve mesaj zorunlu.";
            return RedirectToAction(nameof(Index));
        }

        model.ServiceId = serviceId;
        model.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        model.UserAgent = Request.Headers["User-Agent"].ToString();
        model.IsRead = false;
        model.Status = ContactStatus.New;
        model.CreatedAt = DateTime.UtcNow;

        _db.ContactMessages.Add(model);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Mesajın alındı, en kısa sürede dönüş yapacağım.";
        return RedirectToAction(nameof(Index));
    }
}