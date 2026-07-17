using System.ComponentModel.DataAnnotations;

namespace Portfolio.Models.ViewModels;

public sealed class TwoFactorLoginViewModel
{
    [Required(ErrorMessage = "Authenticator code is required.")]
    [StringLength(16, ErrorMessage = "Enter the code shown by your authenticator app.")]
    [Display(Name = "Authenticator code")]
    public string Code { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public sealed class RecoveryCodeLoginViewModel
{
    [Required(ErrorMessage = "Recovery code is required.")]
    [StringLength(64, ErrorMessage = "Enter a valid recovery code.")]
    [Display(Name = "Recovery code")]
    public string RecoveryCode { get; set; } = string.Empty;
}

public sealed class ManageTwoFactorViewModel
{
    public bool IsEnabled { get; set; }
    public int RecoveryCodesLeft { get; set; }
    public string? SharedKey { get; set; }
    public string? AuthenticatorUri { get; set; }

    [Required(ErrorMessage = "Authenticator code is required.")]
    [StringLength(16, ErrorMessage = "Enter the 6-digit code shown by your authenticator app.")]
    [Display(Name = "Verification code")]
    public string AuthenticatorCode { get; set; } = string.Empty;
}

public sealed class RecoveryCodesViewModel
{
    public IReadOnlyList<string> RecoveryCodes { get; set; } = [];
}
