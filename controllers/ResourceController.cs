using System.Text.RegularExpressions;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer registrering og visning av ressurser.
/// </summary>
public class ResourceController : Controller
{
    private const string PublicActorRole = "PublicActor";
    private const string ResourceProviderRole = "ResourceProvider";

    private readonly ApplicationDbContext _context;


    /// <summary>
    /// Oppretter controlleren med tilgang til databasen.
    /// </summary>
    public ResourceController(ApplicationDbContext context)
    {
        _context = context;
    }


    /// <summary>
    /// Viser skjemaet for registrering av ressurs.
    /// Kun ressursleverandører skal registrere ressurser.
    /// </summary>
    [HttpGet]
    public IActionResult RegisterResource()
    {
        if (!IsLoggedIn())
        {
            return RedirectToHome();
        }

        if (!IsResourceProvider())
        {
            return RedirectToDashboard();
        }

        return View();
    }


    /// <summary>
    /// Registrerer en ny ressurs og lagrer den i databasen.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterResource(
        RegisterResourceViewModel model
    )
    {
        string? userEmail =
            HttpContext.Session.GetString("UserEmail");

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToHome();
        }

        if (!IsResourceProvider())
        {
            return RedirectToDashboard();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (
            model.Latitude == 0
            &&
            model.Longitude == 0
        )
        {
            ModelState.AddModelError(
                string.Empty,
                "Du må velge en plassering i kartet."
            );

            return View(model);
        }

        var resource = new Resource
        {
            ResourceType =
                model.ResourceType!.Value,

            Description =
                model.Description.Trim(),

            Latitude =
                model.Latitude,

            Longitude =
                model.Longitude,

            ContactEmail =
                NormalizeOptionalText(
                    model.ContactEmail
                ),

            ContactPhoneCountryCode =
                string.IsNullOrWhiteSpace(
                    model.ContactPhoneNumber
                )
                    ? null
                    : model.ContactPhoneCountryCode,

            ContactPhoneNumber =
                NormalizePhoneNumber(
                    model.ContactPhoneNumber
                ),

            OwnerEmail =
                userEmail,

            CreatedAt =
                DateTime.UtcNow
        };

        _context.Resources.Add(resource);

        await _context.SaveChangesAsync();

        return RedirectToAction(
            nameof(ResourceOverview)
        );
    }


    /// <summary>
    /// Viser tilgjengelige ressurser.
    ///
    /// Offentlige aktører ser alle ressurser.
    /// Ressursleverandører ser kun egne ressurser.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ResourceOverview()
    {
        string? userEmail =
            HttpContext.Session.GetString("UserEmail");

        string? userRole =
            HttpContext.Session.GetString("UserRole");

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            return RedirectToHome();
        }

        IQueryable<Resource> query =
            _context.Resources
                .AsNoTracking();

        if (userRole == ResourceProviderRole)
        {
            query =
                query.Where(
                    resource =>
                        resource.OwnerEmail == userEmail
                );
        }
        else if (userRole != PublicActorRole)
        {
            return RedirectToDashboard();
        }

        List<Resource> resources =
            await query
                .OrderByDescending(
                    resource =>
                        resource.CreatedAt
                )
                .ToListAsync();

        List<ResourceOverviewViewModel> viewModel =
            resources
                .Select(
                    resource =>
                        new ResourceOverviewViewModel
                        {
                            Id =
                                resource.Id,

                            ResourceType =
                                resource.ResourceType,

                            Description =
                                resource.Description,

                            Position =
                                new Coordinate(
                                    resource.Latitude,
                                    resource.Longitude
                                ),

                            ContactEmail =
                                resource.ContactEmail,

                            ContactPhoneCountryCode =
                                resource.ContactPhoneCountryCode,

                            ContactPhoneNumber =
                                resource.ContactPhoneNumber,

                            OwnerEmail =
                                resource.OwnerEmail,

                            CreatedAt =
                                resource.CreatedAt
                        }
                )
                .ToList();

        return View(viewModel);
    }


    /// <summary>
    /// Kontrollerer om en bruker er logget inn.
    /// Midlertidig Session-løsning frem til Identity innføres.
    /// </summary>
    private bool IsLoggedIn()
    {
        return !string.IsNullOrWhiteSpace(
            HttpContext.Session.GetString(
                "UserEmail"
            )
        );
    }


    /// <summary>
    /// Kontrollerer om innlogget bruker er ressursleverandør.
    /// </summary>
    private bool IsResourceProvider()
    {
        return HttpContext.Session.GetString(
            "UserRole"
        ) == ResourceProviderRole;
    }


    /// <summary>
    /// Sender brukeren tilbake til den offentlige forsiden.
    /// </summary>
    private RedirectToActionResult RedirectToHome()
    {
        return RedirectToAction(
            "Index",
            "Home"
        );
    }


    /// <summary>
    /// Sender brukeren tilbake til dashboardet.
    /// </summary>
    private RedirectToActionResult RedirectToDashboard()
    {
        return RedirectToAction(
            "Dashboard",
            "Home"
        );
    }


    /// <summary>
    /// Fjerner unødvendig mellomrom fra valgfri tekst.
    /// </summary>
    private static string? NormalizeOptionalText(
        string? value
    )
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }


    /// <summary>
    /// Normaliserer telefonnummer før lagring.
    /// </summary>
    private static string? NormalizePhoneNumber(
        string? phoneNumber
    )
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        return Regex.Replace(
            phoneNumber,
            @"\D",
            ""
        );
    }
}