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
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        // Email ile kullanıcıyı bul, sonra username ile giriş yap
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            ModelState.AddModelError("", "E-posta veya şifre yanlış.");
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

        if (result.IsLockedOut)
            ModelState.AddModelError("", "Çok fazla yanlış deneme. 15 dakika bekle.");
        else
            ModelState.AddModelError("", "E-posta veya şifre yanlış.");

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