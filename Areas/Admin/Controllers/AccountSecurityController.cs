using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Data;
using Portfolio.Models.ViewModels;

namespace Portfolio.Areas.Admin.Controllers;

public class AccountSecurityController : AdminBaseController
{
    private const string AuthenticatorIssuer = "Portfolio Admin";
    private const int RecoveryCodeCount = 10;

    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;

    public AccountSecurityController(
        AppDbContext db,
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager) : base(db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
            return Challenge();

        return View(await BuildManageModelAsync(user));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AdminLoginLimit")]
    public async Task<IActionResult> EnableAuthenticator(ManageTwoFactorViewModel model)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
            return Challenge();

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            TempData["Error"] = "Two-factor authentication is already enabled.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
            return View(nameof(Index), await BuildManageModelAsync(user, model.AuthenticatorCode));

        var verificationCode = NormalizeAuthenticatorCode(model.AuthenticatorCode);
        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            verificationCode);

        if (!isValid)
        {
            ModelState.AddModelError(
                nameof(model.AuthenticatorCode),
                "The verification code is invalid. Check the authenticator clock and try again.");
            return View(nameof(Index), await BuildManageModelAsync(user, model.AuthenticatorCode));
        }

        var enableResult = await _userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enableResult.Succeeded)
        {
            AddIdentityErrors(enableResult);
            return View(nameof(Index), await BuildManageModelAsync(user, model.AuthenticatorCode));
        }

        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(
            user,
            RecoveryCodeCount);
        await _signInManager.RefreshSignInAsync(user);

        return View(
            "RecoveryCodes",
            new RecoveryCodesViewModel
            {
                RecoveryCodes = recoveryCodes?.ToArray() ?? []
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateRecoveryCodes()
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
            return Challenge();

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
        {
            TempData["Error"] = "Enable two-factor authentication before generating recovery codes.";
            return RedirectToAction(nameof(Index));
        }

        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(
            user,
            RecoveryCodeCount);

        return View(
            "RecoveryCodes",
            new RecoveryCodesViewModel
            {
                RecoveryCodes = recoveryCodes?.ToArray() ?? []
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AdminLoginLimit")]
    public async Task<IActionResult> ResetAuthenticator(string? currentPassword)
    {
        var user = await GetCurrentUserAsync();
        if (user == null)
            return Challenge();

        if (string.IsNullOrEmpty(currentPassword) ||
            !await _userManager.CheckPasswordAsync(user, currentPassword))
        {
            TempData["Error"] = "The current password is incorrect. The authenticator was not reset.";
            return RedirectToAction(nameof(Index));
        }

        var disableResult = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disableResult.Succeeded)
        {
            TempData["Error"] = "Two-factor authentication could not be disabled for reset.";
            return RedirectToAction(nameof(Index));
        }

        var resetResult = await _userManager.ResetAuthenticatorKeyAsync(user);
        if (!resetResult.Succeeded)
        {
            TempData["Error"] = "The authenticator key could not be reset. Two-factor authentication is currently disabled.";
            return RedirectToAction(nameof(Index));
        }

        await _signInManager.RefreshSignInAsync(user);
        TempData["Success"] = "The authenticator was reset. Configure the new key before signing out.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IdentityUser?> GetCurrentUserAsync() =>
        await _userManager.GetUserAsync(User);

    private async Task<ManageTwoFactorViewModel> BuildManageModelAsync(
        IdentityUser user,
        string? authenticatorCode = null)
    {
        var isEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
        var model = new ManageTwoFactorViewModel
        {
            IsEnabled = isEnabled,
            RecoveryCodesLeft = isEnabled
                ? await _userManager.CountRecoveryCodesAsync(user)
                : 0,
            AuthenticatorCode = authenticatorCode ?? string.Empty
        };

        if (isEnabled)
            return model;

        var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(unformattedKey))
        {
            var resetResult = await _userManager.ResetAuthenticatorKeyAsync(user);
            if (!resetResult.Succeeded)
            {
                AddIdentityErrors(resetResult);
                return model;
            }

            unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
        }

        if (!string.IsNullOrWhiteSpace(unformattedKey))
        {
            model.SharedKey = FormatKey(unformattedKey);
            model.AuthenticatorUri = GenerateAuthenticatorUri(
                await _userManager.GetEmailAsync(user) ?? user.UserName ?? "admin",
                unformattedKey);
        }

        return model;
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
    }

    private static string NormalizeAuthenticatorCode(string code) =>
        code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

    private static string FormatKey(string unformattedKey)
    {
        var result = new System.Text.StringBuilder();
        var currentPosition = 0;

        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }

        if (currentPosition < unformattedKey.Length)
            result.Append(unformattedKey.AsSpan(currentPosition));

        return result.ToString().ToLowerInvariant();
    }

    private static string GenerateAuthenticatorUri(string email, string unformattedKey)
    {
        var encodedIssuer = Uri.EscapeDataString(AuthenticatorIssuer);
        var encodedAccount = Uri.EscapeDataString(email);
        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={unformattedKey}&issuer={encodedIssuer}&digits=6";
    }
}
