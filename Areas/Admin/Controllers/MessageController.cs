using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;
using Portfolio.Services;

namespace Portfolio.Areas.Admin.Controllers;

public class MessageController : AdminBaseController
{
    private const int MaxReplyLength = 5000;
    private readonly TicketEmailOptions _ticketEmailOptions;

    public MessageController(
        AppDbContext db,
        Microsoft.Extensions.Options.IOptions<TicketEmailOptions> ticketEmailOptions)
        : base(db)
    {
        _ticketEmailOptions = ticketEmailOptions.Value;
    }

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
        var message = await LoadMessageAsync(id);

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
        if (!Enum.IsDefined(status) || status == ContactStatus.Replied)
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
    public async Task<IActionResult> SendReply(int id, string? replyBody)
    {
        var message = await LoadMessageAsync(id);
        if (message == null)
            return NotFound();

        var normalizedBody = replyBody?.Trim();

        if (message.Status == ContactStatus.Spam)
        {
            ModelState.AddModelError(
                nameof(replyBody),
                "A reply cannot be sent while this request is marked as spam.");
        }

        if (string.IsNullOrWhiteSpace(normalizedBody))
        {
            ModelState.AddModelError(nameof(replyBody), "Enter a reply.");
        }
        else if (normalizedBody.Length > MaxReplyLength)
        {
            ModelState.AddModelError(
                nameof(replyBody),
                $"The reply cannot exceed {MaxReplyLength} characters.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["ReplyBody"] = replyBody;
            return View(nameof(Detail), message);
        }

        if (!await HasPendingEmailCapacityAsync())
        {
            ModelState.AddModelError(
                nameof(replyBody),
                "The email queue is full. No reply was queued; try again after pending deliveries are processed.");
            ViewData["ReplyBody"] = replyBody;
            return View(nameof(Detail), message);
        }

        _db.TicketEmailOutboxes.Add(new TicketEmailOutbox
        {
            ContactMessageId = message.Id,
            Kind = TicketEmailKinds.TicketReply,
            Body = normalizedBody,
            CreatedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        TempData["Success"] =
            "The reply was queued. The request will be marked Replied only after Resend accepts it.";

        return RedirectToAction(nameof(Detail), new { id });
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

        if (outbox.FailedAt == null)
            return BadRequest();

        if (!await HasPendingEmailCapacityAsync())
        {
            TempData["Error"] =
                "The email queue is full. Process pending deliveries before retrying.";
            return RedirectToAction(nameof(Detail), new { id });
        }

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
    public async Task<IActionResult> RetryReplyEmail(int id, long outboxId)
    {
        var outbox = await _db.TicketEmailOutboxes
            .Include(item => item.ContactMessage)
            .FirstOrDefaultAsync(item =>
                item.Id == outboxId &&
                item.ContactMessageId == id &&
                item.Kind == TicketEmailKinds.TicketReply);

        if (outbox == null)
            return NotFound();

        if (outbox.SentAt != null)
            return BadRequest();

        if (outbox.FailedAt == null)
            return BadRequest();

        if (outbox.ContactMessage.Status == ContactStatus.Spam)
        {
            TempData["Error"] =
                "Change the request from Spam before retrying this reply.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        if (!await HasPendingEmailCapacityAsync())
        {
            TempData["Error"] =
                "The email queue is full. Process pending deliveries before retrying.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        outbox.AttemptCount = 0;
        outbox.NextAttemptAt = DateTime.UtcNow;
        outbox.FailedAt = null;
        outbox.LastErrorCode = null;
        outbox.ProviderMessageId = null;

        await _db.SaveChangesAsync();
        TempData["Success"] = "The reply was queued for another delivery attempt.";

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

    private Task<ContactMessage?> LoadMessageAsync(int id) =>
        _db.ContactMessages
            .Include(message => message.Service)
            .Include(message => message.EmailOutboxItems)
            .FirstOrDefaultAsync(message => message.Id == id);

    private async Task<bool> HasPendingEmailCapacityAsync() =>
        await _db.TicketEmailOutboxes.CountAsync(
            item => item.SentAt == null && item.FailedAt == null) <
        _ticketEmailOptions.MaxPendingItems;
}
