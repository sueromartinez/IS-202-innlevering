
using System.Diagnostics;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Mvc;
using FirstWebAppInDocker.Data;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer forsiden, dashboardet og generell feilhandtering.
/// </summary>
public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

public HomeController(ApplicationDbContext context)
{
    _context = context;
}

    /// <summary>
    /// Viser den offentlige forsiden.
    /// </summary>
    [HttpGet]
    public IActionResult Index()
        {
        return View();
        }

    /// <summary>
    /// Viser dashboardet til en innlogget bruker.
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

            var resources = _context.Resources
    .Where(r => r.OwnerEmail == userEmail)
    .ToList();

    ViewBag.Resources = resources;

    ViewBag.TotalResources = resources.Count;
ViewBag.AvailableResources = resources.Count(r => r.IsAvailable);
ViewBag.UnavailableResources = resources.Count(r => !r.IsAvailable);

        return View();
    }

    /// <summary>
    /// Viser feilside dersom en uventet feil oppstår.
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
                    ?? HttpContext.TraceIdentifier
            }
        );
    }
}
