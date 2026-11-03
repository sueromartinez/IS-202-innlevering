using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer registrering av behov.
/// Kun offentlige aktører kan registrere behov.
/// </summary>
public class NeedController : Controller
{
    private const string PublicActorRole = "PublicActor";

    private readonly ApplicationDbContext _context;


    /// <summary>
    /// Oppretter controlleren med tilgang til databasen.
    /// </summary>
    public NeedController(ApplicationDbContext context)
    {
        _context = context;
    }


    /// <summary>
    /// Viser skjemaet for registrering av behov.
    /// </summary>
    [HttpGet]
    public IActionResult RegisterNeed()
    {
        if (!IsLoggedIn())
        {
            return RedirectToAction("Index", "Home");
        }

        if (!IsPublicActor())
        {
            return RedirectToAction("Dashboard", "Home");
        }

            return View(new RegisterNeedViewModel());
    }


    /// <summary>
    /// Tar imot skjemaet og lagrer behovet i databasen.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterNeed(
        RegisterNeedViewModel model
    )
    {
        string? userEmail =
            HttpContext.Session.GetString("UserEmail");

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToAction("Index", "Home");
        }

        if (!IsPublicActor())
        {
            return RedirectToAction("Dashboard", "Home");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Brukeren må ha valgt et punkt i kartet.
        if (model.Latitude == 0 && model.Longitude == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                "Du må velge en plassering i kartet."
            );

            return View(model);
        }

        var need = new Need
        {
            Title = model.Title.Trim(),
            ResourceType = model.ResourceType!.Value,
            Description = model.Description.Trim(),
            Priority = model.Priority!.Value,
            Latitude = model.Latitude,
            Longitude = model.Longitude,
            OwnerEmail = userEmail,
            CreatedAt = DateTime.UtcNow
        };

        _context.Needs.Add(need);

        await _context.SaveChangesAsync();

        // Tilbake til dashboardet, der behovet nå skal vises.
        return RedirectToAction("Dashboard", "Home");
    }


    /// <summary>
    /// Sjekker om brukeren er logget inn (Session).
    /// </summary>
    private bool IsLoggedIn()
    {
        return !string.IsNullOrWhiteSpace(
            HttpContext.Session.GetString("UserEmail")
        );
    }


    /// <summary>
    /// Sjekker om innlogget bruker er offentlig aktør.
    /// </summary>
    private bool IsPublicActor()
    {
        return HttpContext.Session.GetString("UserRole")
            == PublicActorRole;
    }
}