using System.Text.Json;
using Portfolio.Data;
using Portfolio.Models;
using Portfolio.Models.Enums;

namespace Portfolio.Services;

public interface IAuditService
{
    Task LogAsync(AuditAction action, string entityType, int entityId,
                  string? entityTitle = null, object? oldValues = null, object? newValues = null);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db) => _db = db;

    public async Task LogAsync(AuditAction action, string entityType, int entityId,
                               string? entityTitle = null, object? oldValues = null, object? newValues = null)
    {
        var log = new AuditLog
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            EntityTitle = entityTitle,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues),
            CreatedAt = DateTime.UtcNow
        };

        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}