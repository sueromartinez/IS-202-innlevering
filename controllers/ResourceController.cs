using FirstWebAppInDocker.Services;
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
        // Krever en gyldig periode selv om klientvalidering er slått av.
        if (!model.AvailabilityMonths.HasValue
            || !ResourceAvailability.Periods.ContainsKey(model.AvailabilityMonths.Value))
        {
            ModelState.AddModelError(nameof(model.AvailabilityMonths), "Velg en gyldig tilgjengelighetsperiode.");
        }

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

                // Lagrer valgt periode og sluttdato i eksisterende felt.
                Availability = ResourceAvailability.CreatePeriod(model.AvailabilityMonths!.Value, DateTime.UtcNow),


                // =============================================
                // LOCATION
                // =============================================

                Address =
                    NormalizeOptionalText(
                        model.CombinedAddress()
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


        await this.AuditAsync(
            AuditActions.ResourceCreated,
            $"Registrerte ressursen «{resource.ResourceName}» (#{resource.Id}).");

        TempData["ResourceSuccess"] =
            "Ressursen ble registrert.";

        // Fortell oversikten at den skal vise feiring (gif, konfetti, sang)
        TempData["ResourceCelebrate"] = true;


        return RedirectToAction(
            "Dashboard", "Home"
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

                            // Sender faktiske navn, perioder og adresser til søk og ressurskort.
                            ResourceName = resource.ResourceName,
                            Quantity = resource.Quantity,
                            Capacity = resource.Capacity,
                            Availability = resource.Availability,
                            Address = resource.Address,

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

        await this.AuditAsync(
            AuditActions.ResourceStatusChanged,
            $"Markerte ressursen «{resource.ResourceName}» (#{resource.Id}) som {EnumDisplay.Name(status).ToLowerInvariant()}.");

        TempData["ResourceSuccess"] =
            status == ResourceStatus.Available
                ? "Ressursen er markert som tilgjengelig."
                : "Ressursen er markert som utilgjengelig og vises ikke i søk.";

        return RedirectToAction(
            nameof(ResourceOverview)
        );
    }


    // =========================================================
    // ENDRE RESSURS
    // =========================================================

    /// <summary>
    /// Viser registreringsskjemaet fylt ut med en egen ressurs.
    /// Samme skjema og samme regler som ved registrering.
    /// </summary>
    [HttpGet]
    [Authorize(
        Roles =
            IdentitySeeder.ResourceProviderRole
    )]
    public async Task<IActionResult> EditResource(int id)
    {
        Resource? resource =
            await FindOwnResourceAsync(id);

        if (resource is null)
        {
            return NotFound();
        }

        ViewData["EditResourceId"] = resource.Id;

        return View(
            nameof(RegisterResource),
            ToViewModel(resource)
        );
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(
        Roles =
            IdentitySeeder.ResourceProviderRole
    )]
    public async Task<IActionResult> EditResource(
        int id,
        RegisterResourceViewModel model
    )
    {
        Resource? resource =
            await FindOwnResourceAsync(id);

        if (resource is null)
        {
            return NotFound();
        }

        if (!model.AvailabilityMonths.HasValue
            || !ResourceAvailability.Periods.ContainsKey(model.AvailabilityMonths.Value))
        {
            ModelState.AddModelError(nameof(model.AvailabilityMonths), "Velg en gyldig tilgjengelighetsperiode.");
        }

        if (!model.ResourceType.HasValue)
        {
            ModelState.AddModelError(nameof(model.ResourceType), "Velg type ressurs.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["EditResourceId"] = resource.Id;

            return View(
                nameof(RegisterResource),
                model
            );
        }

        ApplicationUser? user =
            await _userManager.GetUserAsync(User);

        resource.ResourceType = model.ResourceType!.Value;
        resource.ResourceName = model.ResourceName.Trim();
        resource.Description = model.Description.Trim();
        resource.Quantity = model.Quantity;
        resource.Capacity = NormalizeOptionalText(model.Capacity);

        // Perioden regnes fra i dag når ressursen lagres på nytt.
        resource.Availability =
            ResourceAvailability.CreatePeriod(model.AvailabilityMonths!.Value, DateTime.UtcNow);

        resource.Address = NormalizeOptionalText(model.CombinedAddress());
        resource.Latitude = model.Latitude;
        resource.Longitude = model.Longitude;

        resource.ContactEmail =
            string.IsNullOrWhiteSpace(model.ContactEmail)
                ? user?.Email ?? resource.ContactEmail
                : model.ContactEmail.Trim();

        resource.ContactPhoneCountryCode = NormalizeOptionalText(model.ContactPhoneCountryCode);
        resource.ContactPhoneNumber = NormalizeOptionalText(model.ContactPhoneNumber);

        await _dbContext.SaveChangesAsync();

        await this.AuditAsync(
            AuditActions.ResourceUpdated,
            $"Endret ressursen «{resource.ResourceName}» (#{resource.Id}).");

        TempData["ResourceSuccess"] =
            $"Endringene på «{resource.ResourceName}» er lagret.";

        return RedirectToAction(
            nameof(ResourceOverview)
        );
    }


    // =========================================================
    // SLETTE RESSURS
    // =========================================================

    /// <summary>
    /// Lar en ressursleverandør slette sin egen ressurs.
    ///
    /// Samme regler som når admin sletter: en ressurs som er opptatt,
    /// eller som har forespørsler/tildelinger knyttet til seg, slettes ikke,
    /// fordi historikken skal beholdes. Da kan den markeres som utilgjengelig i stedet.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(
        Roles =
            IdentitySeeder.ResourceProviderRole
    )]
    public async Task<IActionResult> DeleteResource(int id)
    {
        Resource? resource =
            await FindOwnResourceAsync(id);

        if (resource is null)
        {
            return NotFound();
        }

        if (resource.Status == ResourceStatus.Busy)
        {
            TempData["ResourceError"] =
                $"«{resource.ResourceName}» er opptatt med et behov og kan ikke slettes nå.";

            return RedirectToAction(nameof(ResourceOverview));
        }

        bool hasHistory =
            await _dbContext.NeedAssignments
                .AnyAsync(assignment => assignment.ResourceId == resource.Id);

        if (hasHistory)
        {
            TempData["ResourceError"] =
                $"«{resource.ResourceName}» har forespørsler knyttet til seg, og historikken må beholdes. " +
                "Marker den som utilgjengelig i stedet.";

            return RedirectToAction(nameof(ResourceOverview));
        }

        string name = resource.ResourceName;
        int resourceId = resource.Id;

        _dbContext.Resources.Remove(resource);

        await _dbContext.SaveChangesAsync();

        await this.AuditAsync(
            AuditActions.ResourceDeleted,
            $"Slettet ressursen «{name}» (#{resourceId}).");

        TempData["ResourceSuccess"] =
            $"«{name}» er slettet.";

        return RedirectToAction(nameof(ResourceOverview));
    }


    /// <summary>
    /// Finner en ressurs som tilhører innlogget bruker,
    /// også eldre ressurser som bare er koblet via e-post.
    /// </summary>
    private async Task<Resource?> FindOwnResourceAsync(int id)
    {
        ApplicationUser? user =
            await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return null;
        }

        string? email = user.Email;

        return await _dbContext.Resources
            .FirstOrDefaultAsync(resource =>
                resource.Id == id
                && (resource.OwnerUserId == user.Id
                    || (resource.OwnerUserId == null && email != null && resource.OwnerEmail == email)));
    }


    /// <summary>
    /// Gjør en lagret ressurs om til skjemamodellen, slik at skjemaet kan fylles ut.
    /// Adressen ble lagret som «gate, postnummer poststed – kommune kommune»
    /// og deles opp igjen i de tre feltene.
    /// </summary>
    private static RegisterResourceViewModel ToViewModel(Resource resource)
    {
        string? street = resource.Address;
        string? postal = null;
        string? municipality = null;

        string[] mainParts =
            resource.Address?.Split(" – ", 2) ?? Array.Empty<string>();

        // Bare adresser i formatet fra skjemaet deles opp. Alt annet
        // (f.eks. eldre fritekst) beholdes uendret i adressefeltet.
        if (mainParts.Length == 2 && mainParts[1].EndsWith(" kommune", StringComparison.Ordinal))
        {
            municipality = mainParts[1][..^" kommune".Length].Trim();

            int comma = mainParts[0].IndexOf(", ", StringComparison.Ordinal);

            if (comma > 0)
            {
                street = mainParts[0][..comma].Trim();
                postal = mainParts[0][(comma + 2)..].Trim();
            }
            else
            {
                street = mainParts[0].Trim();
            }
        }

        // Finn perioden som ble valgt sist, f.eks. "6 måneder; til 2027-05-01".
        int? months =
            ResourceAvailability.Periods
                .Where(period => resource.Availability?.StartsWith(period.Value + ";") == true)
                .Select(period => (int?)period.Key)
                .FirstOrDefault();

        return new RegisterResourceViewModel
        {
            ResourceType = resource.ResourceType,
            ResourceName = resource.ResourceName,
            Description = resource.Description,
            Quantity = resource.Quantity,
            Capacity = resource.Capacity,
            AvailabilityMonths = months,
            Address = resource.Address,
            StreetAddress = street,
            PostalLocation = postal,
            Municipality = municipality,
            Latitude = resource.Latitude,
            Longitude = resource.Longitude,
            ContactEmail = resource.ContactEmail,
            ContactPhoneCountryCode = resource.ContactPhoneCountryCode ?? "+47",
            ContactPhoneNumber = resource.ContactPhoneNumber
        };
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
