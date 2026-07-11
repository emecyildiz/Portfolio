using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;

namespace Portfolio.Controllers;

public class HireController : BaseController
{
    public HireController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var services = await _db.Services
            .Where(s => s.Status == VisibilityStatus.Public)
            .Include(s => s.References)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();
        ViewBag.Services = services;
        ViewBag.CvUrl = (await _db.SiteSettings.FirstOrDefaultAsync())?.CvFileUrl;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ContactFormLimit")]
    public async Task<IActionResult> Contact(ContactMessage model, int? serviceId, string? website)
    {
        if (!string.IsNullOrEmpty(website))
        {
            TempData["Success"] = "Talebin alındı!";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        if (string.IsNullOrWhiteSpace(model.Name) ||
            string.IsNullOrWhiteSpace(model.Email) ||
            string.IsNullOrWhiteSpace(model.Message))
        {
            TempData["Error"] = "Ad, e-posta ve mesaj zorunlu.";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        model.TicketNumber = Guid.NewGuid();
        model.ServiceId = serviceId;
        model.IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        model.UserAgent = Request.Headers["User-Agent"].ToString();
        model.IsRead = false;
        model.Status = ContactStatus.New;
        model.CreatedAt = DateTime.UtcNow;

        _db.ContactMessages.Add(model);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Talebin alındı!";
        TempData["TicketNumber"] = model.TicketNumber.ToString();
        return RedirectToAction("Index", "Hire", new { area = "" });
    }

    [HttpPost]
    public async Task<IActionResult> TrackTicket(string ticketNumber)
    {
        if (!Guid.TryParse(ticketNumber, out var guid))
        {
            TempData["TrackError"] = "Geçersiz bilet numarası formatı.";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        var message = await _db.ContactMessages
            .FirstOrDefaultAsync(m => m.TicketNumber == guid);

        if (message == null)
        {
            TempData["TrackError"] = "Bu bilet numarasına ait talep bulunamadı.";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        var services = await _db.Services
            .Where(s => s.Status == VisibilityStatus.Public)
            .Include(s => s.References)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        ViewBag.Services = services;
        ViewBag.TrackedMessage = message;
        return View("Index");
    }
}