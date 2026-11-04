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


        ViewBag.UserRole =
            userRole;


        var model =
            new DashboardViewModel();


        // =====================================================
        // PUBLIC ACTOR / ADMIN
        // =====================================================

        if (
            userRole
                == IdentitySeeder.PublicActorRole
            ||
            userRole
                == IdentitySeeder.AdminRole
        )
        {
            model =
                new DashboardViewModel
                {
                    Needs =
                        await _context.Needs
                            .AsNoTracking()
                            .OrderByDescending(
                                need =>
                                    need.CreatedAt
                            )
                            .ToListAsync(),

                    Resources =
                        await _context.Resources
                            .AsNoTracking()
                            .OrderByDescending(
                                resource =>
                                    resource.CreatedAt
                            )
                            .ToListAsync()
                };
        }


        // =====================================================
        // RESOURCE PROVIDER
        // =====================================================

        if (
            userRole
                == IdentitySeeder.ResourceProviderRole
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

                                    // Midlertidig støtte for
                                    // gamle ressurser som ble
                                    // opprettet før OwnerUserId.
                                    (
                                        resource.OwnerUserId
                                            == null

                                        &&

                                        resource.OwnerEmail
                                            == userEmail
                                    )
                            )
                            .OrderByDescending(
                                resource =>
                                    resource.CreatedAt
                            )
                            .ToListAsync()
                };
        }


        return View(
            model
        );
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