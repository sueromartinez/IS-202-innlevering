using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.Controllers;

public class HomeController : Controller
{
    private static List<PositionModel> resources = new List<PositionModel>();

    // Viser skjema-siden for å registrere ressurs
    public IActionResult RegisterResource()
    {
        return View();
    }

    // Tar imot dataene fra skjemaet
    [HttpPost]
    public IActionResult RegisterResource(PositionModel model)
    {
        if (ModelState.IsValid)
        {
            resources.Add(model);
            return RedirectToAction("ResourceOverview");
        }
        return View(model);
    }

    // Viser oversikts-siden med alle registrerte ressurser
    public IActionResult ResourceOverview()
    {
        return View(resources);
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}