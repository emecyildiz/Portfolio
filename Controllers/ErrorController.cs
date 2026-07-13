using Microsoft.AspNetCore.Mvc;

namespace Portfolio.Controllers;

public class ErrorController : Controller
{
    [Route("Error/{code}")]
    public IActionResult Index(int code)
    {
        if (code == 404)
            return View("~/Views/Shared/Error.cshtml");

        // Diğer hata kodları için de aynı sayfayı kullanabiliriz, mesaj değişebilir
        return View("~/Views/Shared/Error.cshtml");
    }
}