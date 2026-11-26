/*
    HomeController.cs – forsiden og dashbordet

    Hva filen gjør: Viser forsiden og dashbordet. Dashbordet har forskjellig innhold for hver
    rolle.

    På nettsiden: Forsiden med informasjon og innlogging, og «Oversikt»-siden man kommer til
    etter innlogging.

    Oppgavekrav: 13, 18, 19
*/

using System.Diagnostics;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/*
    Håndterer forsiden, dashboardet
    og generell feilhåndtering.
*/
public class HomeController : Controller
{
    #region Felt og konstruktør

    private readonly ApplicationDbContext _context;

    private readonly UserManager<ApplicationUser>
        _userManager;

    public HomeController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    #endregion

    #region Forside

    /*
        GET / – forsiden. Er man allerede innlogget, sendes man rett til dashbordet.

        Viser forsiden for besøkende.

        Brukere som allerede er innlogget
        sendes direkte til Dashboard.

        Dette gjør at en gyldig innloggingscookie
        også gjenkjennes når nettleseren åpnes
        på nytt.
    */
    [HttpGet]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(Dashboard));
        }

        return View();
    }

    #endregion

    #region Dashbord

    /*
        GET /Home/Dashboard – krever innlogging. Rollen hentes fra Identity.

        Viser dashboardet til en innlogget bruker.

        Ressursleverandører ser kun egne ressurser.
        Eierskap bestemmes primært gjennom
        ASP.NET Core Identity sin bruker-ID.

        Offentlige aktører får behov og
        ressurser fra databasen.
    */
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Dashboard()
    {
        ApplicationUser? user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        string userRole;

        if (User.IsInRole(IdentitySeeder.AdminRole))
        {
            userRole = IdentitySeeder.AdminRole;
        }
        else if (User.IsInRole(IdentitySeeder.PublicActorRole))
        {
            userRole = IdentitySeeder.PublicActorRole;
        }
        else
        {
            userRole = IdentitySeeder.ResourceProviderRole;
        }

        /*
            Identity er sannhetskilden
            for innlogging og brukerroller.
        */

        ViewBag.UserName =
            string.IsNullOrWhiteSpace(user.Name)
                ? user.Email
                : user.Name;

        ViewBag.UserRole = userRole;

        DashboardViewModel model = new DashboardViewModel();

        // --- Public actor / admin ---

        /*
            Offentlig aktør og admin: alle uløste behov og alle ressurser, til tellerne og
            kartet.
        */
        if (userRole == IdentitySeeder.PublicActorRole || userRole == IdentitySeeder.AdminRole)
        {
            model =
                new DashboardViewModel
                {
                    Needs =
                        await _context.Needs
                            .AsNoTracking()
                            // Løste behov skal ikke vises i kartet, tellerne eller listen.
                            .Where(need => need.Status != NeedStatus.Resolved)
                            .OrderByDescending(need => need.CreatedAt)
                            .ToListAsync(),

                    Resources =
                        await _context.Resources
                            .AsNoTracking()
                            .OrderByDescending(resource => resource.CreatedAt)
                            .ToListAsync()
                };
        }

        // --- Ressursleverandør ---

        // Frivillig: bare egne ressurser, til listen og nivået/poengene.
        if (userRole == IdentitySeeder.ResourceProviderRole)
        {
            string? userEmail = user.Email;

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

                                    /*
                                        Støtte for eldre ressurser
                                        uten OwnerUserId.
                                    */
                                    (
                                        resource.OwnerUserId
                                            == null

                                        &&

                                        resource.OwnerEmail
                                            == userEmail)
                            )
                            .OrderByDescending(resource => resource.CreatedAt)
                            .ToListAsync()
                };
        }

        return View(model);
    }

    #endregion

    #region Feilside

    // GET /Home/Error
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
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

    #endregion
}
