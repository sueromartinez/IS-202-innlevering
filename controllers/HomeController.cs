using System.Diagnostics;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer forsiden, dashboardet
/// og generell feilhandtering.
/// </summary>
public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    private readonly UserManager<ApplicationUser>
        _userManager;


    /// <summary>
    /// Oppretter controlleren med tilgang
    /// til databasen og ASP.NET Core Identity.
    /// </summary>
    public HomeController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager
    )
    {
        _context = context;
        _userManager = userManager;
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
    ///
    /// Ressursleverandører ser kun egne ressurser.
    /// Eierskap bestemmes primært gjennom
    /// ASP.NET Core Identity sin bruker-ID.
    ///
    /// Offentlige aktører får behov og
    /// ressurser fra databasen.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        ApplicationUser? user =
            await _userManager.GetUserAsync(
                User
            );


        if (user is null)
        {
            return Challenge();
        }


        string userRole;


        if (
            User.IsInRole(
                IdentitySeeder.AdminRole
            )
        )
        {
            userRole =
                IdentitySeeder.AdminRole;
        }
        else if (
            User.IsInRole(
                IdentitySeeder.PublicActorRole
            )
        )
        {
            userRole =
                IdentitySeeder.PublicActorRole;
        }
        else
        {
            userRole =
                IdentitySeeder.ResourceProviderRole;
        }


        // Session beholdes midlertidig fordi
        // eksisterende dashboard-view bruker den
        // enkelte steder.
        //
        // Identity er sannhetskilden.
        ViewBag.UserName =
            string.IsNullOrWhiteSpace(
                user.Name
            )
                ? user.Email
                : user.Name;


        ViewBag.UserRole = userRole;

        DashboardViewModel model;

        // Offentlige aktører og administratorer ser
        // alle behov og ressurser.
        if (
            userRole == IdentitySeeder.PublicActorRole ||
            userRole == IdentitySeeder.AdminRole
        )
        {
            model = new DashboardViewModel
            {
                Needs = await _context.Needs
                    .AsNoTracking()
                    .OrderByDescending(need => need.CreatedAt)
                    .ToListAsync(),

                Resources = await _context.Resources
                    .AsNoTracking()
                    .OrderByDescending(resource => resource.CreatedAt)
                    .ToListAsync()
            };
        }
        else
        {
            // Ressursleverandører ser bare egne ressurser.
            // Identity-ID brukes primært, e-post er reserve
            // for eldre ressurser uten OwnerUserId.
            string? userEmail = user.Email;

            var resources = await _context.Resources
                .AsNoTracking()
                .Where(resource =>
                    resource.OwnerUserId == user.Id ||
                    (
                        resource.OwnerUserId == null &&
                        resource.OwnerEmail == userEmail
                    )
                )
                .OrderByDescending(resource => resource.CreatedAt)
                .ToListAsync();

            model = new DashboardViewModel
            {
                Resources = resources
            };

            ViewBag.TotalResources = resources.Count;
            ViewBag.AvailableResources = resources.Count(r => r.IsAvailable);
            ViewBag.UnavailableResources = resources.Count(r => !r.IsAvailable);
        }

        return View(model);
    }


    /// <summary>
    /// Viser feilside dersom en
    /// uventet feil oppstår.
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