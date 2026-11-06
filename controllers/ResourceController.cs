using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer registrering og visning av ressurser.
///
/// ASP.NET Core Identity brukes for autentisering,
/// roller og eierskap.
/// </summary>
[Authorize(
    Roles =
        IdentitySeeder.ResourceProviderRole
        + ","
        + IdentitySeeder.PublicActorRole
        + ","
        + IdentitySeeder.AdminRole
)]
public class ResourceController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    private readonly UserManager<ApplicationUser> _userManager;


    public ResourceController(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager
    )
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }


    // =========================================================
    // REGISTER RESOURCE - GET
    // =========================================================

    [HttpGet]
    [Authorize(
        Roles =
            IdentitySeeder.ResourceProviderRole
    )]
    public IActionResult RegisterResource()
    {
        return View(
            new RegisterResourceViewModel()
        );
    }


    // =========================================================
    // REGISTER RESOURCE - POST
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(
        Roles =
            IdentitySeeder.ResourceProviderRole
    )]
    public async Task<IActionResult> RegisterResource(
        RegisterResourceViewModel model
    )
    {
        if (!ModelState.IsValid)
        {
            return View(
                model
            );
        }


        if (!model.ResourceType.HasValue)
        {
            ModelState.AddModelError(
                nameof(model.ResourceType),
                "Velg type ressurs."
            );

            return View(
                model
            );
        }


        ApplicationUser? user =
            await _userManager.GetUserAsync(
                User
            );


        if (
            user is null
            ||
            string.IsNullOrWhiteSpace(
                user.Email
            )
        )
        {
            return Challenge();
        }


        var resource =
            new Resource
            {
                // =============================================
                // RESOURCE INFORMATION
                // =============================================

                ResourceType =
                    model.ResourceType.Value,

                ResourceName =
                    model.ResourceName.Trim(),

                Description =
                    model.Description.Trim(),

                Quantity =
                    model.Quantity,

                Capacity =
                    NormalizeOptionalText(
                        model.Capacity
                    ),

                Availability =
                    NormalizeOptionalText(
                        model.Availability
                    ),


                // =============================================
                // LOCATION
                // =============================================

                Address =
                    NormalizeOptionalText(
                        model.Address
                    ),

                Latitude =
                    model.Latitude,

                Longitude =
                    model.Longitude,


                // =============================================
                // CONTACT
                // =============================================

                ContactEmail =
                    string.IsNullOrWhiteSpace(
                        model.ContactEmail
                    )
                        ? user.Email
                        : model.ContactEmail.Trim(),

                ContactPhoneCountryCode =
                    NormalizeOptionalText(
                        model.ContactPhoneCountryCode
                    ),

                ContactPhoneNumber =
                    NormalizeOptionalText(
                        model.ContactPhoneNumber
                    ),


                // =============================================
                // OWNERSHIP
                // =============================================
                //
                // OwnerUserId er den permanente
                // koblingen mellom ressurs og bruker.
                //
                // OwnerEmail beholdes foreløpig
                // for bakoverkompatibilitet.
                //

                OwnerUserId =
                    user.Id,

                OwnerEmail =
                    user.Email,


                // =============================================
                // METADATA
                // =============================================

                CreatedAt =
                    DateTime.UtcNow
            };


        _dbContext.Resources.Add(
            resource
        );


        await _dbContext
            .SaveChangesAsync();


        TempData["ResourceSuccess"] =
            "Ressursen ble registrert.";


        return RedirectToAction(
            nameof(ResourceOverview)
        );
    }


    // =========================================================
    // RESOURCE OVERVIEW
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> ResourceOverview()
    {
        ApplicationUser? user =
            await _userManager.GetUserAsync(
                User
            );


        if (
            user is null
            ||
            string.IsNullOrWhiteSpace(
                user.Email
            )
        )
        {
            return Challenge();
        }


        bool isPublicActor =
            User.IsInRole(
                IdentitySeeder.PublicActorRole
            );


        bool isAdmin =
            User.IsInRole(
                IdentitySeeder.AdminRole
            );


        bool isResourceProvider =
            User.IsInRole(
                IdentitySeeder.ResourceProviderRole
            );


        // =============================================
        // LINK LEGACY RESOURCES
        // =============================================
        //
        // Eldre ressurser kan være opprettet før
        // OwnerUserId ble innført.
        //
        // Dersom en slik ressurs har samme
        // OwnerEmail som den innloggede brukeren,
        // kobles den nå permanent til brukerens
        // Identity-ID.
        //
        // Dette gjør at ressursen fortsatt tilhører
        // brukeren selv om e-postadressen endres.
        //

        if (
            isResourceProvider
            &&
            !isPublicActor
            &&
            !isAdmin
        )
        {
            await LinkLegacyResourcesToIdentityUserAsync(
                user
            );
        }


        IQueryable<Resource> query =
            _dbContext.Resources
                .AsNoTracking();


        // Ressursleverandører ser bare
        // ressurser de selv eier.
        if (
            isResourceProvider
            &&
            !isPublicActor
            &&
            !isAdmin
        )
        {
            query =
                query.Where(
                    resource =>
                        resource.OwnerUserId
                            == user.Id

                        ||

                        (
                            resource.OwnerUserId
                                == null

                            &&

                            resource.OwnerEmail
                                == user.Email
                        )
                );
        }


        List<ResourceOverviewViewModel> resources =
            await query
                .OrderByDescending(
                    resource =>
                        resource.CreatedAt
                )
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
                                resource.CreatedAt,

                            Status =
                                resource.Status
                        }
                )
                .ToListAsync();


        if (isAdmin)
        {
            ViewBag.UserRole =
                IdentitySeeder.AdminRole;
        }
        else if (isPublicActor)
        {
            ViewBag.UserRole =
                IdentitySeeder.PublicActorRole;
        }
        else
        {
            ViewBag.UserRole =
                IdentitySeeder.ResourceProviderRole;
        }


        return View(
            resources
        );
    }


    // =========================================================
    // ENDRE TILGJENGELIGHET
    // =========================================================

    /// <summary>
    /// Lar eieren markere en ressurs som tilgjengelig eller
    /// utilgjengelig. "Opptatt" settes bare av systemet når
    /// en forespørsel er bekreftet.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(
        Roles =
            IdentitySeeder.ResourceProviderRole
    )]
    public async Task<IActionResult> SetStatus(
        int id,
        ResourceStatus status
    )
    {
        if (status == ResourceStatus.Busy)
        {
            return BadRequest();
        }

        string? userId =
            _userManager.GetUserId(
                User
            );

        Resource? resource =
            await _dbContext.Resources
                .FirstOrDefaultAsync(
                    item =>
                        item.Id == id
                        && item.OwnerUserId == userId
                );

        if (resource is null)
        {
            return NotFound();
        }

        if (resource.Status == ResourceStatus.Busy)
        {
            TempData["ResourceSuccess"] =
                "Ressursen er opptatt med et behov. Den blir ledig når behovet er løst eller forespørselen avbrutt.";

            return RedirectToAction(
                nameof(ResourceOverview)
            );
        }

        resource.Status =
            status;

        await _dbContext
            .SaveChangesAsync();

        TempData["ResourceSuccess"] =
            status == ResourceStatus.Available
                ? "Ressursen er markert som tilgjengelig."
                : "Ressursen er markert som utilgjengelig og vises ikke i søk.";

        return RedirectToAction(
            nameof(ResourceOverview)
        );
    }


    // =========================================================
    // LEGACY OWNERSHIP HELPER
    // =========================================================

    /// <summary>
    /// Kobler eldre ressurser som bare har
    /// OwnerEmail til brukerens permanente
    /// ASP.NET Core Identity-ID.
    ///
    /// Dette er overgangslogikk for ressurser
    /// som ble opprettet før OwnerUserId
    /// ble innført.
    /// </summary>
    private async Task
        LinkLegacyResourcesToIdentityUserAsync(
            ApplicationUser user
        )
    {
        if (
            string.IsNullOrWhiteSpace(
                user.Email
            )
        )
        {
            return;
        }


        List<Resource> legacyResources =
            await _dbContext.Resources
                .Where(
                    resource =>
                        resource.OwnerUserId
                            == null

                        &&

                        resource.OwnerEmail
                            == user.Email
                )
                .ToListAsync();


        if (legacyResources.Count == 0)
        {
            return;
        }


        foreach (
            Resource resource
            in legacyResources
        )
        {
            resource.OwnerUserId =
                user.Id;
        }


        await _dbContext
            .SaveChangesAsync();
    }


    // =========================================================
    // HELPERS
    // =========================================================

    /// <summary>
    /// Gjør tom eller whitespace-tekst om til null.
    /// Ellers fjernes unødvendige mellomrom
    /// foran og bak teksten.
    /// </summary>
    private static string? NormalizeOptionalText(
        string? value
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                value
            )
        )
        {
            return null;
        }


        return value.Trim();
    }
}