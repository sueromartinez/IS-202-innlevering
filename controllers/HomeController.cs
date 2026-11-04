using System.Diagnostics;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer forsiden, dashboardet og generell feilhandtering.
/// </summary>
public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    /// <summary>
    /// Oppretter controlleren med tilgang til databasen.
    /// </summary>
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
    /// Offentlige aktører får behov og ressurser fra databasen.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        string? userEmail =
            HttpContext.Session.GetString("UserEmail");

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction(nameof(Index));
        }

        string? userRole =
            HttpContext.Session.GetString("UserRole");

        ViewBag.UserName =
            HttpContext.Session.GetString("UserName");

        ViewBag.UserRole = userRole;

        var model = new DashboardViewModel();

        // Kun offentlige aktører ser situasjonsbildet.
        if (userRole == "PublicActor")
        {
            model = new DashboardViewModel
            {
                Needs =
                    await _context.Needs
                        .AsNoTracking()
                        .OrderByDescending(need => need.CreatedAt)
                        .ToListAsync(),

                Resources =
                    await _context.Resources
                        .AsNoTracking()
                        .OrderByDescending(resource => resource.CreatedAt)
                        .ToListAsync()
            };
        }

        // Loads only the signed-in provider's resources so the dashboard cannot expose other owners.
        if (userRole == "ResourceProvider")
        {
            model = new DashboardViewModel
            {
                Resources = await _context.Resources.AsNoTracking()
                    .Where(resource => resource.OwnerEmail == userEmail)
                    .OrderByDescending(resource => resource.CreatedAt)
                    .ToListAsync()
            };
        }

        return View(model);
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
