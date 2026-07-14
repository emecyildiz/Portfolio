using Microsoft.AspNetCore.Mvc;

namespace Portfolio.Controllers;

public class ProductionErrorController : Controller
{
    [Route("Home/Error")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View("~/Views/Shared/ServerError.cshtml");
    }
}
