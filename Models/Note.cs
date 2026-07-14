using Portfolio.Models.Enums;

namespace Portfolio.Models
{
    public class Note
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;       // Markdown or plain text

        public NoteType NoteType { get; set; } = NoteType.Idea;

        public string? RelatedUrl { get; set; }    // Optional destination for the Current Focus widget

        // These fields are active in task mode.
        public bool IsTodo { get; set; } = false;
        public bool IsCompleted { get; set; } = false;
        public DateOnly? DueDate { get; set; }

        public NotePriority Priority { get; set; } = NotePriority.Normal;

        // Simple string labels; no separate tag system is needed.
        // Example: ["cybersecurity", "ESP32", "review later"]
        public string? TagsJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
