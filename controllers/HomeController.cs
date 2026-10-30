using System.Diagnostics;
<<<<<<< HEAD
using Microsoft.AspNetCore.Mvc;
using FirstWebAppInDocker.Models;
=======
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Mvc;
>>>>>>> origin/main

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer forsiden, dashboardet og generell feilhåndtering.
/// </summary>
public class HomeController : Controller
{
    /// <summary>
<<<<<<< HEAD
    /// Viser applikasjonens offentlige forside.
    /// </summary>
=======
    /// Viser den offentlige forsiden.
    /// </summary>
    [HttpGet]
>>>>>>> origin/main
    public IActionResult Index()
    {
        return View();
    }


    /// <summary>
    /// Viser dashboardet til en innlogget bruker.
<<<<<<< HEAD
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
=======
    /// Innholdet tilpasses brukerens rolle.
    /// </summary>
    [HttpGet]
    public IActionResult Dashboard()
    {
        string? userEmail =
            HttpContext.Session.GetString("UserEmail");

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction(
                nameof(Index)
            );
        }

        ViewBag.UserName =
            HttpContext.Session.GetString(
                "UserName"
            );

        ViewBag.UserRole =
            HttpContext.Session.GetString(
                "UserRole"
            );
>>>>>>> origin/main

        return View();
    }


    /// <summary>
<<<<<<< HEAD
    /// Viser personvernsiden.
    /// </summary>
    public IActionResult Privacy()
    {
        return View();
    }


    /// <summary>
    /// Viser informasjon dersom det oppstår en feil.
=======
    /// Viser feilside dersom en uventet feil oppstår.
>>>>>>> origin/main
    /// </summary>
    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
<<<<<<< HEAD
        NoStore = true)]
=======
        NoStore = true
    )]
>>>>>>> origin/main
    public IActionResult Error()
    {
        return View(
            new ErrorViewModel
            {
                RequestId =
                    Activity.Current?.Id
                    ?? HttpContext.TraceIdentifier
<<<<<<< HEAD
            });
=======
            }
        );
>>>>>>> origin/main
    }
} 