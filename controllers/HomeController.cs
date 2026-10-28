using System.Diagnostics;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer generelle sider i applikasjonen.
/// </summary>
public class HomeController : Controller
{
    /// <summary>
    /// Viser forsiden.
    /// </summary>
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }


    /// <summary>
    /// Viser dashboardet til innlogget bruker.
    /// </summary>
    [HttpGet]
    public IActionResult Dashboard()
    {
        string? userEmail =
            HttpContext.Session.GetString(
                "UserEmail"
            );

        string? userName =
            HttpContext.Session.GetString(
                "UserName"
            );

        string? userRole =
            HttpContext.Session.GetString(
                "UserRole"
            );


        if (string.IsNullOrEmpty(userEmail))
        {
            return RedirectToAction(
                nameof(Index)
            );
        }


        ViewBag.UserName =
            userName;

        ViewBag.UserRole =
            userRole;


        return View();
    }


    /// <summary>
    /// Viser informasjon om personvern.
    /// </summary>
    [HttpGet]
    public IActionResult Privacy()
    {
        return View();
    }


    /// <summary>
    /// Viser feilside dersom en ukjent feil oppstår.
    /// </summary>
    [ResponseCache(
        Duration = 0,
        Location = ResponseCacheLocation.None,
        NoStore = true
    )]
    public IActionResult Error()
    {
        return View(
            new ErrorViewModel
            {
                RequestId =
                    Activity.Current?.Id
                    ??
                    HttpContext.TraceIdentifier
            }
        );
    }
}