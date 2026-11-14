
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer registrering, oversikt, endring og sletting av behov.
///
/// ASP.NET Core Identity brukes til autentisering,
/// rollebasert tilgang og permanent eierskap.
///
/// Nye behov kobles til OwnerUserId.
/// Eldre behov kan kobles til Identity via OwnerEmail.
/// </summary>
[Authorize(
    Roles =
        IdentitySeeder.PublicActorRole
        + ","
        + IdentitySeeder.AdminRole
)]
public class NeedController : Controller
{
    private readonly ApplicationDbContext _context;

    private readonly UserManager<ApplicationUser> _userManager;


    public NeedController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }


    // ========================================
    // OVERSIKT
    // ========================================

    /// <summary>
    /// Viser alle registrerte behov, nyeste først.
    ///
    /// Knytter samtidig eventuelle eldre behov
    /// til den innloggede brukerens Identity-ID.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> NeedOverview()
    {
        IActionResult? accessFailure = CheckAccess();

        if (accessFailure is not null)
        {
            return accessFailure;
        }

        ApplicationUser? user =
            await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        // Oppgrader eventuelle eldre behov som
        // ennå ikke har permanent bruker-ID.
        await LinkLegacyNeedsToIdentityUserAsync(user);

        List<Need> needs = await _context.Needs
            .AsNoTracking()
            .OrderByDescending(need => need.CreatedAt)
            .ToListAsync();

        // Hastegrad (0–100) for hvert behov, vises som merke i listen.
        // Lages direkte her, så konstruktøren og testene ikke må endres.
        ViewData["Urgency"] =
            await new FirstWebAppInDocker.Services.Matching.NeedUrgencyService(_context)
                .CalculateAsync(needs);

        return View(needs);
    }


    // ========================================
    // REGISTRER - GET
    // ========================================

    [HttpGet]
    public async Task<IActionResult> RegisterNeed()
    {
        IActionResult? accessFailure = CheckAccess();

        if (accessFailure is not null)
        {
            return accessFailure;
        }

        var model = new RegisterNeedsViewModel();

        ApplicationUser? currentUser =
            await _userManager.GetUserAsync(User);

        // Skjemaet starter med ett tomt behov.
        // Kontaktpunktet fylles ut med brukerens eget navn og telefon.
        model.Needs.Add(new NeedItemViewModel
        {
            ContactName = currentUser?.Name,
            ContactPhone = currentUser?.PhoneNumber
        });

        return View(model);
    }


    // ========================================
    // REGISTRER - POST
    // ========================================

    /// <summary>
    /// Registrerer ett eller flere behov.
    ///
    /// Eierens permanente Identity-ID og e-post
    /// hentes fra den innloggede brukeren.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterNeed(
        RegisterNeedsViewModel model)
    {
        IActionResult? accessFailure = CheckAccess();

        if (accessFailure is not null)
        {
            return accessFailure;
        }

        ApplicationUser? user =
            await _userManager.GetUserAsync(User);

        if (
            user is null
            ||
            string.IsNullOrWhiteSpace(user.Email)
        )
        {
            return Challenge();
        }

        if (model.Needs.Count == 0)
        {
            model.Needs.Add(new NeedItemViewModel());

            ModelState.AddModelError(
                string.Empty,
                "Legg til minst ett behov."
            );

            return View(model);
        }

        // Kontroller at alle behov har
        // ressurstype og kartplassering.
        for (int i = 0; i < model.Needs.Count; i++)
        {
            NeedItemViewModel item = model.Needs[i];

            if (
                item.Latitude is null
                ||
                item.Longitude is null
            )
            {
                ModelState.AddModelError(
                    $"Needs[{i}].Latitude",
                    "Velg plassering i kartet."
                );
            }

            if (!item.ResourceType.HasValue)
            {
                ModelState.AddModelError(
                    $"Needs[{i}].ResourceType",
                    "Velg type behov."
                );
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        foreach (NeedItemViewModel item in model.Needs)
        {
            var need = new Need
            {
                ResourceType = item.ResourceType!.Value,

                Priority = item.Priority,

                Quantity = item.Quantity,

                Description = item.Description.Trim(),

                Latitude = item.Latitude!.Value,

                Longitude = item.Longitude!.Value,

                Deadline = item.Deadline?.Date,

                ContactName = NormalizeText(item.ContactName),

                ContactPhone = NormalizeText(item.ContactPhone),

                // Permanent eierskap.
                OwnerUserId = user.Id,

                // Beholdes for bakoverkompatibilitet.
                OwnerEmail = user.Email
            };

            _context.Needs.Add(need);
        }

        await _context.SaveChangesAsync();

        await this.AuditAsync(
            AuditActions.NeedCreated,
            $"Registrerte {model.Needs.Count} behov.");

        TempData["NeedMessage"] =
            $"{model.Needs.Count} behov ble registrert.";

        return RedirectToAction(
            nameof(NeedOverview)
        );
    }


    // ========================================
    // ENDRE - GET
    // ========================================

    /// <summary>
    /// Viser skjemaet for å endre et behov.
    /// Brukeren kan bare endre egne behov.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> EditNeed(int id)
    {
        IActionResult? accessFailure = CheckAccess();

        if (accessFailure is not null)
        {
            return accessFailure;
        }

        ApplicationUser? user =
            await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        Need? need = await FindOwnNeedAsync(id, user);

        if (need is null)
        {
            return NotFound();
        }

        var model = new NeedItemViewModel
        {
            Id = need.Id,

            ResourceType = need.ResourceType,

            Priority = need.Priority,

            Quantity = need.Quantity,

            Description = need.Description,

            Latitude = need.Latitude,

            Longitude = need.Longitude,

            Deadline = need.Deadline,

            ContactName = need.ContactName,

            ContactPhone = need.ContactPhone
        };

        return View(model);
    }


    // ========================================
    // ENDRE - POST
    // ========================================

    /// <summary>
    /// Lagrer endringer på et behov som
    /// tilhører den innloggede brukeren.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditNeed(
        NeedItemViewModel model)
    {
        IActionResult? accessFailure = CheckAccess();

        if (accessFailure is not null)
        {
            return accessFailure;
        }

        ApplicationUser? user =
            await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        Need? need = await FindOwnNeedAsync(
            model.Id,
            user
        );

        if (need is null)
        {
            return NotFound();
        }

        if (
            model.Latitude is null
            ||
            model.Longitude is null
        )
        {
            ModelState.AddModelError(
                nameof(model.Latitude),
                "Velg plassering i kartet."
            );
        }

        if (!model.ResourceType.HasValue)
        {
            ModelState.AddModelError(
                nameof(model.ResourceType),
                "Velg type behov."
            );
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        need.ResourceType = model.ResourceType!.Value;

        need.Priority = model.Priority;

        need.Quantity = model.Quantity;

        need.Description = model.Description.Trim();

        need.Latitude = model.Latitude!.Value;

        need.Longitude = model.Longitude!.Value;

        need.Deadline = model.Deadline?.Date;

        need.ContactName = NormalizeText(model.ContactName);

        need.ContactPhone = NormalizeText(model.ContactPhone);

        // OwnerUserId og OwnerEmail endres ikke.
        await _context.SaveChangesAsync();

        await this.AuditAsync(
            AuditActions.NeedUpdated,
            $"Endret behov #{need.Id} ({EnumDisplay.Name(need.ResourceType)}).");

        TempData["NeedMessage"] =
            "Behovet ble oppdatert.";

        return RedirectToAction(
            nameof(NeedOverview)
        );
    }


    // ========================================
    // SLETT - POST
    // ========================================

    /// <summary>
    /// Sletter et behov som tilhører innlogget bruker.
    /// Kun POST med antiforgery-token.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNeed(int id)
    {
        IActionResult? accessFailure = CheckAccess();

        if (accessFailure is not null)
        {
            return accessFailure;
        }

        ApplicationUser? user =
            await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        Need? need = await FindOwnNeedAsync(id, user);

        if (need is null)
        {
            return NotFound();
        }

        string deletedText =
            $"Slettet behov #{need.Id} ({EnumDisplay.Name(need.ResourceType)}).";

        _context.Needs.Remove(need);

        await _context.SaveChangesAsync();

        await this.AuditAsync(
            AuditActions.NeedDeleted,
            deletedText);

        TempData["NeedMessage"] =
            "Behovet ble slettet.";

        return RedirectToAction(
            nameof(NeedOverview)
        );
    }


    /// <summary>
    /// Tom tekst lagres som null.
    /// </summary>
    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }


    // ========================================
    // HJELPEMETODE - FINN EGET BEHOV
    // ========================================

    /// <summary>
    /// Finner et behov dersom det tilhører brukeren.
    ///
    /// Først kontrolleres OwnerUserId.
    ///
    /// Eldre behov uten OwnerUserId kan identifiseres
    /// via OwnerEmail og kobles permanent til brukeren.
    ///
    /// Dersom et behov allerede har OwnerUserId
    /// til en annen bruker, gir ikke lik e-post
    /// tilgang til behovet.
    /// </summary>
    private async Task<Need?> FindOwnNeedAsync(
        int id,
        ApplicationUser user)
    {
        Need? need = await _context.Needs
            .FirstOrDefaultAsync(
                need =>
                    need.Id == id
                    &&
                    (
                        need.OwnerUserId == user.Id
                        ||
                        (
                            need.OwnerUserId == null
                            &&
                            need.OwnerEmail == user.Email
                        )
                    )
            );

        if (need is null)
        {
            return null;
        }

        // Oppgrader eldre behov til permanent eierskap.
        if (need.OwnerUserId is null)
        {
            need.OwnerUserId = user.Id;

            await _context.SaveChangesAsync();
        }

        return need;
    }


    // ========================================
    // HJELPEMETODE - KOBLE ELDRE BEHOV
    // ========================================

    /// <summary>
    /// Finner eldre behov som ble opprettet før
    /// OwnerUserId ble innført.
    ///
    /// Dersom OwnerEmail samsvarer med brukerens
    /// nåværende Identity-e-post, knyttes behovene
    /// permanent til brukerens Identity-ID.
    ///
    /// Eksisterende e-postopplysninger beholdes.
    /// </summary>
    private async Task LinkLegacyNeedsToIdentityUserAsync(
        ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        List<Need> legacyNeeds = await _context.Needs
            .Where(
                need =>
                    need.OwnerUserId == null
                    &&
                    need.OwnerEmail == user.Email
            )
            .ToListAsync();

        if (legacyNeeds.Count == 0)
        {
            return;
        }

        foreach (Need need in legacyNeeds)
        {
            need.OwnerUserId = user.Id;
        }

        await _context.SaveChangesAsync();
    }


    // ========================================
    // HJELPEMETODE - TILGANGSKONTROLL
    // ========================================

    /// <summary>
    /// Kontrollerer autentisering og rolle
    /// gjennom ASP.NET Core Identity.
    ///
    /// Session brukes ikke som sikkerhetskontroll.
    /// </summary>
    private IActionResult? CheckAccess()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Challenge();
        }

        bool isPublicActor = User.IsInRole(
            IdentitySeeder.PublicActorRole
        );

        bool isAdmin = User.IsInRole(
            IdentitySeeder.AdminRole
        );

        if (!isPublicActor && !isAdmin)
        {
            return Forbid();
        }

        return null;
    }
}
