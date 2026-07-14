using System.ComponentModel.DataAnnotations;

namespace Portfolio.Models.ViewModels;

public sealed class ContactRequestViewModel
{
    [Required(ErrorMessage = "Name is required.")]
    [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(300, ErrorMessage = "Email cannot exceed 300 characters.")]
    public string Email { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Subject cannot exceed 200 characters.")]
    public string? Subject { get; set; }

    [Required(ErrorMessage = "Message is required.")]
    [StringLength(5000, ErrorMessage = "Message cannot exceed 5000 characters.")]
    public string Message { get; set; } = string.Empty;

    public int? ServiceId { get; set; }

    [StringLength(200)]
    public string? Website { get; set; }
}
