using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;

namespace Portfolio.Areas.Admin.Controllers;

public class MessageController : AdminBaseController
{
    public MessageController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var messages = await _db.ContactMessages
            .Include(m => m.Service)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

        return View(messages);
    }

    public async Task<IActionResult> Detail(int id)
    {
        var message = await _db.ContactMessages
            .Include(m => m.Service)
            .Include(m => m.EmailOutboxItems)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (message == null) return NotFound();

        // Mark the message as read.
        if (!message.IsRead)
        {
            message.IsRead = true;
            message.Status = ContactStatus.Read;
            await _db.SaveChangesAsync();
        }

        return View(message);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkStatus(int id, ContactStatus status)
    {
        if (!Enum.IsDefined(status))
            return BadRequest();

        var message = await _db.ContactMessages.FindAsync(id);
        if (message == null) return NotFound();

        message.Status = status;
        if (status != ContactStatus.New)
            message.IsRead = true;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RetryConfirmationEmail(int id)
    {
        var outbox = await _db.TicketEmailOutboxes
            .FirstOrDefaultAsync(item =>
                item.ContactMessageId == id &&
                item.Kind == TicketEmailKinds.TicketReceived);

        if (outbox == null)
            return NotFound();

        if (outbox.SentAt != null)
            return BadRequest();

        outbox.AttemptCount = 0;
        outbox.NextAttemptAt = DateTime.UtcNow;
        outbox.FailedAt = null;
        outbox.LastErrorCode = null;
        outbox.ProviderMessageId = null;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var message = await _db.ContactMessages.FindAsync(id);
        if (message == null) return NotFound();

        _db.ContactMessages.Remove(message);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
