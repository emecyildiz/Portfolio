using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Portfolio.Models.ViewModels;

namespace Portfolio.Areas.Admin.Controllers;

[Area("Admin")]
public class AccountController : Controller
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;


    public AccountController(SignInManager<IdentityUser> signInManager,
                             UserManager<IdentityUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [HttpGet]
    public IActionResult Login() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AdminLoginLimit")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        // Find the user by email, then sign in with the username.
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            ModelState.AddModelError("", "The email or password is incorrect.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            user.UserName!,
            model.Password,
            isPersistent: model.RememberMe,
            lockoutOnFailure: true
        );

        if (result.Succeeded)
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });

        if (result.RequiresTwoFactor)
        {
            return RedirectToAction(
                nameof(LoginWithTwoFactor),
                new { rememberMe = model.RememberMe });
        }

        if (result.IsLockedOut)
            ModelState.AddModelError("", "Too many failed attempts. Try again in 15 minutes.");
        else
            ModelState.AddModelError("", "The email or password is incorrect.");

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> LoginWithTwoFactor(bool rememberMe = false)
    {
        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
            return RedirectToAction(nameof(Login));

        return View(new TwoFactorLoginViewModel { RememberMe = rememberMe });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AdminLoginLimit")]
    public async Task<IActionResult> LoginWithTwoFactor(TwoFactorLoginViewModel model)
    {
        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
            return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid)
            return View(model);

        var authenticatorCode = model.Code
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(
            authenticatorCode,
            model.RememberMe,
            rememberClient: false);

        if (result.Succeeded)
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });

        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "Too many failed attempts. Try again in 15 minutes.");
        else
            ModelState.AddModelError(string.Empty, "The authenticator code is invalid.");

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> LoginWithRecoveryCode()
    {
        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
            return RedirectToAction(nameof(Login));

        return View(new RecoveryCodeLoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("AdminLoginLimit")]
    public async Task<IActionResult> LoginWithRecoveryCode(RecoveryCodeLoginViewModel model)
    {
        if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
            return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid)
            return View(model);

        var recoveryCode = model.RecoveryCode
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

        if (result.Succeeded)
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });

        if (result.IsLockedOut)
            ModelState.AddModelError(string.Empty, "Too many failed attempts. Try again in 15 minutes.");
        else
            ModelState.AddModelError(string.Empty, "The recovery code is invalid or has already been used.");

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login", "Account", new { area = "Admin" });
    }
}
