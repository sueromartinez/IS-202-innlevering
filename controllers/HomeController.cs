using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer generelle sider i applikasjonen.
/// </summary>
public class HomeController : Controller
{
    /// <summary>
    /// Viser applikasjonens offentlige forside.
    /// </summary>
    public IActionResult Index()
    {
        return View();
    }


    /// <summary>
    /// Viser dashboardet til en innlogget bruker.
    /// Innholdet kan tilpasses basert på brukerens rolle.
    /// </summary>
    public IActionResult Dashboard()
    {
        // Henter brukerdata fra Session.
        string? userEmail =
            HttpContext.Session.GetString("UserEmail");

        string? userName =
            HttpContext.Session.GetString("UserName");

        string? userRole =
            HttpContext.Session.GetString("UserRole");


        // Dersom brukeren ikke er logget inn,
        // sendes brukeren tilbake til forsiden.
        if (string.IsNullOrEmpty(userEmail))
        {
            return RedirectToAction(nameof(Index));
        }


        // Sender informasjonen videre til Dashboard-viewet.
        ViewBag.UserName = userName;
        ViewBag.UserRole = userRole;

        return View();
    }


    /// <summary>
    /// Viser personvernsiden.
    /// </summary>
    public IActionResult Privacy()
    {
        return View();
    }


    /// <summary>
    /// Viser informasjon dersom det oppstår en feil.
    /// </summary>
    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult Error()
    {
        return View(
            new ErrorViewModel
            {
                RequestId =
                    Activity.Current?.Id
                    ?? HttpContext.TraceIdentifier
            });
    }
} 