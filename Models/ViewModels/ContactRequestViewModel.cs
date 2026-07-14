using System.ComponentModel.DataAnnotations;

namespace Portfolio.Models.ViewModels;

public sealed class ContactRequestViewModel
{
    [Required(ErrorMessage = "Ad zorunlu.")]
    [StringLength(100, ErrorMessage = "Ad en fazla 100 karakter olabilir.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-posta zorunlu.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi gir.")]
    [StringLength(300, ErrorMessage = "E-posta en fazla 300 karakter olabilir.")]
    public string Email { get; set; } = string.Empty;

    [StringLength(200, ErrorMessage = "Konu en fazla 200 karakter olabilir.")]
    public string? Subject { get; set; }

    [Required(ErrorMessage = "Mesaj zorunlu.")]
    [StringLength(5000, ErrorMessage = "Mesaj en fazla 5000 karakter olabilir.")]
    public string Message { get; set; } = string.Empty;

    public int? ServiceId { get; set; }

    [StringLength(200)]
    public string? Website { get; set; }
}
