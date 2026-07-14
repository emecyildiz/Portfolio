using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Models.ViewModels;

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
    public async Task<IActionResult> Contact(ContactRequestViewModel request)
    {
        if (!string.IsNullOrEmpty(request.Website))
        {
            TempData["Success"] = "Talebin alındı!";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Form alanlarını ve e-posta adresini kontrol et.";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        if (request.ServiceId.HasValue && !await _db.Services.AnyAsync(s =>
                s.Id == request.ServiceId.Value && s.Status == VisibilityStatus.Public))
        {
            TempData["Error"] = "Seçilen hizmet geçerli değil.";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        var message = new ContactMessage
        {
            TicketNumber = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Email = request.Email.Trim(),
            Subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim(),
            Message = request.Message.Trim(),
            ServiceId = request.ServiceId,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers["User-Agent"].ToString(),
            IsRead = false,
            Status = ContactStatus.New,
            CreatedAt = DateTime.UtcNow
        };

        _db.ContactMessages.Add(message);
        await _db.SaveChangesAsync();

        TempData["Success"] = "Talebin alındı!";
        TempData["TicketNumber"] = message.TicketNumber.ToString();
        return RedirectToAction("Index", "Hire", new { area = "" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
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
