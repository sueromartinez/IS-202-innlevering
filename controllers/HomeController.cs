
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
/// og generell feilhåndtering.
/// </summary>
public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    private readonly UserManager<ApplicationUser>
        _userManager;

    public HomeController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager
    )
    {
        _context = context;
        _userManager = userManager;
    }

    // ========================================
    // FORSIDE
    // ========================================

    /// <summary>
    /// Viser forsiden for besøkende.
    ///
    /// Brukere som allerede er innlogget
    /// sendes direkte til Dashboard.
    ///
    /// Dette gjør at en gyldig innloggingscookie
    /// også gjenkjennes når nettleseren åpnes
    /// på nytt.
    /// </summary>
    [HttpGet]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(
                nameof(Dashboard)
            );
        }

        return View();
    }

    // ========================================
    // DASHBOARD
    // ========================================

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

        // Identity er sannhetskilden
        // for innlogging og brukerroller.

        ViewBag.UserName =
            string.IsNullOrWhiteSpace(
                user.Name
            )
                ? user.Email
                : user.Name;

        ViewBag.UserRole =
            userRole;

        var model =
            new DashboardViewModel();

        // ========================================
        // PUBLIC ACTOR / ADMIN
        // ========================================

        if (
            userRole == IdentitySeeder.PublicActorRole
            ||
            userRole == IdentitySeeder.AdminRole
        )
        {
            model =
                new DashboardViewModel
                {
                    Needs =
                        await _context.Needs
                            .AsNoTracking()
                            .OrderByDescending(
                                need => need.CreatedAt
                            )
                            .ToListAsync(),

                    Resources =
                        await _context.Resources
                            .AsNoTracking()
                            .OrderByDescending(
                                resource => resource.CreatedAt
                            )
                            .ToListAsync()
                };
        }

        // ========================================
        // RESOURCE PROVIDER
        // ========================================

        if (
            userRole == IdentitySeeder.ResourceProviderRole
        )
        {
            string? userEmail =
                user.Email;

            model =
                new DashboardViewModel
                {
                    Resources =
                        await _context.Resources
                            .AsNoTracking()
                            .Where(
                                resource =>

                                    // Permanent eierskapskobling.
                                    resource.OwnerUserId
                                        == user.Id

                                    ||

                                    // Støtte for eldre ressurser
                                    // uten OwnerUserId.
                                    (
                                        resource.OwnerUserId
                                            == null

                                        &&

                                        resource.OwnerEmail
                                            == userEmail
                                    )
                            )
                            .OrderByDescending(
                                resource => resource.CreatedAt
                            )
                            .ToListAsync()
                };
        }

        return View(model);
    }

    // ========================================
    // FEILSIDE
    // ========================================

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
