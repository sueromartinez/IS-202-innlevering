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
    private readonly ApplicationDbContext _context;


    /// <summary>
    /// Oppretter controlleren med tilgang til databasen.
    /// </summary>
    /// <param name="context">
    /// Databasekonteksten som brukes mot MariaDB.
    /// </param>
    public ResourceController(
        ApplicationDbContext context
    )
    {
        _context = context;
    }


    /// <summary>
    /// Viser skjemaet for registrering av en ressurs.
    /// </summary>
    [HttpGet]
    public IActionResult RegisterResource()
    {
        string? userEmail =
            HttpContext.Session.GetString(
                "UserEmail"
            );


        if (string.IsNullOrEmpty(userEmail))
        {
            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        return View();
    }


    /// <summary>
    /// Registrerer en ny ressurs og lagrer den
    /// permanent i MariaDB.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterResource(
        RegisterResourceViewModel model
    )
    {
        string? userEmail =
            HttpContext.Session.GetString(
                "UserEmail"
            );


        if (string.IsNullOrEmpty(userEmail))
        {
            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        /*
         * Tidlig retur dersom valideringen
         * av skjemaet feiler.
         */
        if (!ModelState.IsValid)
        {
            return View(model);
        }


        /*
         * Brukeren må velge et punkt i kartet.
         */
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


        string? normalizedPhoneNumber =
            NormalizePhoneNumber(
                model.ContactPhoneNumber
            );


        var resource =
            new Resource
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
                    string.IsNullOrWhiteSpace(
                        model.ContactEmail
                    )
                        ? null
                        : model.ContactEmail.Trim(),

                ContactPhoneCountryCode =
                    string.IsNullOrWhiteSpace(
                        normalizedPhoneNumber
                    )
                        ? null
                        : model.ContactPhoneCountryCode,

                ContactPhoneNumber =
                    normalizedPhoneNumber,

                OwnerEmail =
                    userEmail,

                CreatedAt =
                    DateTime.UtcNow
            };


        /*
         * Legger ressursen til i EF Core.
         */
        _context.Resources.Add(
            resource
        );


        /*
         * Lagrer ressursen permanent i MariaDB.
         */
        await _context.SaveChangesAsync();


        return RedirectToAction(
            nameof(ResourceOverview)
        );
    }


    /// <summary>
    /// Viser ressursoversikten.
    /// Offentlige aktører ser alle ressurser.
    /// Ressursleverandører ser kun egne ressurser.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ResourceOverview()
    {
        string? userEmail =
            HttpContext.Session.GetString(
                "UserEmail"
            );

        string? userRole =
            HttpContext.Session.GetString(
                "UserRole"
            );


        if (string.IsNullOrEmpty(userEmail))
        {
            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        IQueryable<Resource> query =
            _context.Resources
                .AsNoTracking();


        /*
         * Offentlig aktør får se alle registrerte
         * ressurser.
         */
        if (userRole == "PublicActor")
        {
            // Beholder hele queryen.
        }

        /*
         * Ressursleverandører får kun se
         * ressursene de selv eier.
         */
        else if (userRole == "ResourceProvider")
        {
            query =
                query.Where(
                    resource =>
                        resource.OwnerEmail
                        == userEmail
                );
        }

        /*
         * Ukjent rolle får ikke tilgang.
         */
        else
        {
            return RedirectToAction(
                "Dashboard",
                "Home"
            );
        }


        List<Resource> resources =
            await query
                .OrderByDescending(
                    resource =>
                        resource.CreatedAt
                )
                .ToListAsync();


        /*
         * Mapper databaseobjektene til PositionModel.
         *
         * Dette gjør at frontend ikke trenger
         * å kjenne til database-entiteten Resource.
         */
        List<PositionModel> viewModel =
            resources
                .Select(
                    resource =>
                        new PositionModel
                        {
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
                                resource.OwnerEmail
                        }
                )
                .ToList();


        return View(
            viewModel
        );
    }


    /// <summary>
    /// Normaliserer telefonnummeret før det
    /// lagres i databasen.
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