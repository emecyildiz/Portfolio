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
    private const int DailyPerIpSubmissionLimit = 8;
    private const int DailyGlobalSubmissionLimit = 60;

    public HireController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var services = await _db.Services
            .Where(s => s.Status == VisibilityStatus.Public)
            .Include(s => s.References)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();
        ViewBag.Services = services;
        ViewBag.CvUrl = (await _db.SiteSettings
            .OrderBy(settings => settings.Id)
            .FirstOrDefaultAsync())?.CvFileUrl;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ContactFormLimit")]
    public async Task<IActionResult> Contact(ContactRequestViewModel request)
    {
        if (!string.IsNullOrEmpty(request.Website))
        {
            TempData["Success"] = "Your request has been received!";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please check the form fields and email address.";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        if (request.ServiceId.HasValue && !await _db.Services.AnyAsync(s =>
                s.Id == request.ServiceId.Value && s.Status == VisibilityStatus.Public))
        {
            TempData["Error"] = "The selected service is not valid.";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp?.IsIPv4MappedToIPv6 == true)
            remoteIp = remoteIp.MapToIPv4();

        var now = DateTime.UtcNow;
        var requestWindowStart = now.AddHours(-24);
        var ipAddress = remoteIp?.ToString();

        var recentGlobalSubmissions = await _db.ContactMessages.CountAsync(
            message => message.CreatedAt >= requestWindowStart);
        if (recentGlobalSubmissions >= DailyGlobalSubmissionLimit)
        {
            TempData["Error"] =
                "The request channel is temporarily at capacity. Please try again later.";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        if (ipAddress is not null)
        {
            var recentIpSubmissions = await _db.ContactMessages.CountAsync(
                message =>
                    message.IpAddress == ipAddress &&
                    message.CreatedAt >= requestWindowStart);

            if (recentIpSubmissions >= DailyPerIpSubmissionLimit)
            {
                TempData["Error"] =
                    "Too many requests have been submitted from this connection. Please try again later.";
                return RedirectToAction("Index", "Hire", new { area = "" });
            }
        }

        var userAgent = Request.Headers.UserAgent.ToString().Trim();
        if (userAgent.Length > 512)
            userAgent = userAgent[..512];

        var message = new ContactMessage
        {
            TicketNumber = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Email = request.Email.Trim(),
            Subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim(),
            Message = request.Message.Trim(),
            ServiceId = request.ServiceId,
            IpAddress = ipAddress,
            UserAgent = string.IsNullOrEmpty(userAgent) ? null : userAgent,
            IsRead = false,
            Status = ContactStatus.New,
            CreatedAt = now
        };

        _db.ContactMessages.Add(message);
        _db.TicketEmailOutboxes.Add(new TicketEmailOutbox
        {
            ContactMessage = message,
            Kind = TicketEmailKinds.TicketReceived,
            NextAttemptAt = now,
            CreatedAt = now
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "Your request has been received!";
        TempData["TicketNumber"] = message.TicketNumber.ToString();
        TempData["ConfirmationEmailQueued"] = true;
        return RedirectToAction("Index", "Hire", new { area = "" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("TicketTrackingLimit")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> TrackTicket(string ticketNumber)
    {
        if (!Guid.TryParse(ticketNumber, out var guid))
        {
            TempData["TrackError"] = "The ticket number format is invalid.";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        var message = await _db.ContactMessages
            .FirstOrDefaultAsync(m => m.TicketNumber == guid);

        if (message == null)
        {
            TempData["TrackError"] = "No request was found for this ticket number.";
            return RedirectToAction("Index", "Hire", new { area = "" });
        }

        var services = await _db.Services
            .Where(s => s.Status == VisibilityStatus.Public)
            .Include(s => s.References)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        ViewBag.Services = services;
        ViewBag.TrackedMessage = message;
        ViewBag.TrackedTicketNumber = guid.ToString("D");
        return View("Index");
    }
}
