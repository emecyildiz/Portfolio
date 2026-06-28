using System.ComponentModel.DataAnnotations;

namespace Portfolio.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "E-posta zorunlu")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta gir")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunlu")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}