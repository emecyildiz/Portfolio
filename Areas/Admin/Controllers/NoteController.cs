using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;

namespace Portfolio.Areas.Admin.Controllers;

public class NoteController : AdminBaseController
{
    public NoteController(AppDbContext db) : base(db) { }

    public async Task<IActionResult> Index()
    {
        var notes = await _db.Notes
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        return View(notes);
    }

    public IActionResult Create() => View(new Note());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Note model)
    {
        if (!ModelState.IsValid)
            return View(model);

        model.CreatedAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;

        _db.Notes.Add(model);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var note = await _db.Notes.FindAsync(id);
        if (note == null) return NotFound();
        return View(note);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Note model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var existing = await _db.Notes.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Title = model.Title;
        existing.Content = model.Content;
        existing.NoteType = model.NoteType;
        existing.IsTodo = model.IsTodo;
        existing.IsCompleted = model.IsCompleted;
        existing.DueDate = model.DueDate;
        existing.Priority = model.Priority;
        existing.RelatedUrl = model.RelatedUrl;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleComplete(int id)
    {
        var note = await _db.Notes.FindAsync(id);
        if (note == null) return NotFound();

        note.IsCompleted = !note.IsCompleted;
        note.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var note = await _db.Notes.FindAsync(id);
        if (note == null) return NotFound();

        _db.Notes.Remove(note);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
