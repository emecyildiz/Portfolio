using Microsoft.AspNetCore.Mvc;

namespace Portfolio.Controllers;

public class ErrorController : Controller
{
    [Route("Error/{code}")]
    public IActionResult Index(int code)
    {
        if (code == 404)
            return View("~/Views/Shared/Error.cshtml");

        // Reuse the shared error page for other status codes.
        return View("~/Views/Shared/Error.cshtml");
    }
}
