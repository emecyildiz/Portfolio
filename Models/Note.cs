using Portfolio.Models.Enums;

namespace Portfolio.Models
{
    public class Note
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;       // Markdown veya düz metin

        public NoteType NoteType { get; set; } = NoteType.Idea;

        // Todo modunda bu alanlar aktif
        public bool IsTodo { get; set; } = false;
        public bool IsCompleted { get; set; } = false;
        public DateOnly? DueDate { get; set; }

        public NotePriority Priority { get; set; } = NotePriority.Normal;

        // Basit string etiketler — ayrı tag sistemine gerek yok
        // Örnek: ["siber güvenlik", "ESP32", "ileride bak"]
        public string? TagsJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
