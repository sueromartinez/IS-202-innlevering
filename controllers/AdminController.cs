
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Brukeradministrasjon. Kun for Admin.
///
/// Administrator kan:
/// - Se alle brukere.
/// - Opprette offentlige aktører.
/// - Endre roller.
/// - Deaktivere og reaktivere kontoer.
/// - Se ressursene til en valgt bruker.
/// - Slette ressurser uten tildelingshistorikk.
///
/// Brukerkontoer slettes ikke.
/// Historikk over tildelinger skal beholdes.
/// </summary>
[Authorize(Roles = IdentitySeeder.AdminRole)]
public class AdminController : Controller
{
    private static readonly string[] AllowedRoles =
    {
        IdentitySeeder.ResourceProviderRole,
        IdentitySeeder.PublicActorRole,
        IdentitySeeder.AdminRole
    };

    private readonly UserManager<ApplicationUser> _userManager;

    private readonly ApplicationDbContext? _context;

    private readonly ILogger<AdminController>? _logger;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext? context = null,
        ILogger<AdminController>? logger = null
    )
    {
        _userManager = userManager;
        _context = context;
        _logger = logger;
    }

    // =========================================================
    // BRUKEROVERSIKT
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Users()
    {
        string? currentUserId =
            _userManager.GetUserId(User);

        List<ApplicationUser> users =
            await _userManager.Users
                .AsNoTracking()
                .OrderBy(user => user.Name)
                .ToListAsync();

        var model = new List<AdminUserViewModel>();

        foreach (ApplicationUser user in users)
        {
            IList<string> roles =
                await _userManager.GetRolesAsync(user);

            bool isLocked =
                user.LockoutEnd.HasValue
                && user.LockoutEnd.Value
                    > DateTimeOffset.UtcNow;

            bool isDeactivated =
                isLocked
                && user.LockoutEnd!.Value
                    > DateTimeOffset.UtcNow.AddYears(50);

            model.Add(new AdminUserViewModel
            {
                Id = user.Id,

                Name =
                    string.IsNullOrWhiteSpace(user.Name)
                        ? "(uten navn)"
                        : user.Name,

                Email = user.Email ?? string.Empty,

                Role =
                    roles.FirstOrDefault()
                    ?? "Ingen rolle",

                IsDeactivated = isDeactivated,

                IsTemporarilyLocked =
                    isLocked && !isDeactivated,

                IsCurrentUser =
                    user.Id == currentUserId,

                CreatedAt = user.CreatedAt
            });
        }

        return View(model);
    }

    // =========================================================
    // SE RESSURSER TIL EN BRUKER
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> UserResources(
        string userId
    )
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest(
                "Bruker-ID mangler."
            );
        }

        ApplicationUser? user =
            await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return NotFound(
                "Brukeren finnes ikke."
            );
        }

        IList<string> roles =
            await _userManager.GetRolesAsync(user);

        string role =
            roles.FirstOrDefault()
            ?? "Ingen rolle";

        // ========================================
        // BRUKERINFORMASJON TIL VIEW
        // ========================================

        ViewBag.ManagedUserId =
            user.Id;

        ViewBag.ManagedUserName =
            string.IsNullOrWhiteSpace(user.Name)
                ? "(uten navn)"
                : user.Name;

        ViewBag.ManagedUserEmail =
            user.Email ?? string.Empty;

        ViewBag.ManagedUserPhone =
            user.PhoneNumber ?? string.Empty;

        ViewBag.ManagedUserRole =
            role;

        ViewBag.ManagedUserCreatedAt =
            user.CreatedAt;

        if (_context is null)
        {
            throw new InvalidOperationException(
                "Databasetilkoblingen er ikke tilgjengelig."
            );
        }

        // ========================================
        // HENT RESSURSENE
        // ========================================

        string? ownerEmail =
            user.Email;

        // OwnerUserId er hovedkoblingen.
        //
        // Eldre ressurser kan mangle OwnerUserId.
        // Da brukes OwnerEmail som reserve.
        //
        // Ressurser som tilhører en annen
        // bruker-ID skal aldri tas med.

        List<Resource> resources =
            await _context.Resources
                .AsNoTracking()
                .Where(resource =>

                    resource.OwnerUserId == user.Id

                    ||

                    (
                        resource.OwnerUserId == null
                        &&
                        ownerEmail != null
                        &&
                        resource.OwnerEmail == ownerEmail
                    )
                )
                .OrderByDescending(
                    resource => resource.CreatedAt
                )
                .ToListAsync();

        return View(resources);
    }

    // =========================================================
    // NYTT: SLETT RESSURS SOM ADMIN
    // =========================================================

    /// <summary>
    /// Sletter en ressurs som tilhører den
    /// angitte brukeren.
    ///
    /// Sikkerhetskontroller:
    ///
    /// 1. Kun Admin har tilgang.
    /// 2. POST med antiforgery-token.
    /// 3. Brukeren må eksistere.
    /// 4. Ressursen må tilhøre brukeren.
    /// 5. Opptatte ressurser kan ikke slettes.
    /// 6. Ressurser med tildelingshistorikk
    ///    kan ikke slettes.
    ///
    /// Dette hindrer at vi mister historikk
    /// gjennom cascade delete.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUserResource(
        string userId,
        int resourceId
    )
    {
        // ========================================
        // KONTROLLER INNDATA
        // ========================================

        if (string.IsNullOrWhiteSpace(userId)
            || resourceId <= 0)
        {
            return BadRequest(
                "Ugyldig bruker eller ressurs."
            );
        }

        // ========================================
        // KONTROLLER BRUKER
        // ========================================

        ApplicationUser? user =
            await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return NotFound(
                "Brukeren finnes ikke."
            );
        }

        if (_context is null)
        {
            throw new InvalidOperationException(
                "Databasetilkoblingen er ikke tilgjengelig."
            );
        }

        // ========================================
        // FINN RESSURSEN OG KONTROLLER EIERSKAP
        // ========================================

        Resource? resource =
            await _context.Resources
                .FirstOrDefaultAsync(item =>

                    item.Id == resourceId

                    &&

                    (
                        item.OwnerUserId == user.Id

                        ||

                        (
                            item.OwnerUserId == null
                            &&
                            user.Email != null
                            &&
                            item.OwnerEmail == user.Email
                        )
                    )
                );

        if (resource is null)
        {
            // Ikke slett en ressurs som ikke
            // eksisterer eller ikke tilhører
            // den valgte brukeren.

            return NotFound(
                "Ressursen finnes ikke hos denne brukeren."
            );
        }

        // ========================================
        // BESKYTT OPPTATTE RESSURSER
        // ========================================

        if (resource.Status == ResourceStatus.Busy)
        {
            TempData["AdminError"] =
                $"Ressursen «{resource.ResourceName}» " +
                "kan ikke slettes fordi den er opptatt " +
                "med et behov.";

            return RedirectToAction(
                nameof(UserResources),
                new { userId }
            );
        }

        // ========================================
        // KONTROLLER TILDELINGSHISTORIKK
        // ========================================

        // Databaseoppsettet har cascade delete
        // mellom Resource og NeedAssignment.
        //
        // Derfor må vi kontrollere at ingen
        // tildelinger er knyttet til ressursen.
        //
        // Vi beskytter både:
        // - Ventende forespørsler
        // - Bekreftede tildelinger
        // - Avslåtte forespørsler
        // - Avbrutte forespørsler
        //
        // Alle disse kan være viktige
        // for historikken i Beredskapsportalen.

        bool hasAssignments =
            await _context.NeedAssignments
                .AsNoTracking()
                .AnyAsync(assignment =>
                    assignment.ResourceId == resource.Id
                );

        if (hasAssignments)
        {
            TempData["AdminError"] =
                $"Ressursen «{resource.ResourceName}» " +
                "kan ikke slettes fordi den har " +
                "tilknyttede forespørsler eller " +
                "tildelingshistorikk. " +
                "Historikken må beholdes.";

            return RedirectToAction(
                nameof(UserResources),
                new { userId }
            );
        }

        // ========================================
        // SLETT RESSURSEN
        // ========================================

        int deletedResourceId =
            resource.Id;

        string deletedResourceName =
            resource.ResourceName;

        _context.Resources.Remove(resource);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            // Dersom databasen avviser sletting,
            // beholder vi ressursen og viser
            // en forståelig feilmelding.
            //
            // Dette kan blant annet skje dersom
            // en ny tilknytning oppstår samtidig
            // som administrator forsøker å slette.

            _logger?.LogError(
                exception,
                "Administrator kunne ikke slette " +
                "ressurs {ResourceId} for bruker {UserId}.",
                deletedResourceId,
                userId
            );

            TempData["AdminError"] =
                "Ressursen kunne ikke slettes. " +
                "Prøv igjen eller kontroller om " +
                "den har nye tilknytninger.";

            return RedirectToAction(
                nameof(UserResources),
                new { userId }
            );
        }

        // ========================================
        // LOGGFØR HANDLINGEN
        // ========================================

        string? adminId =
            _userManager.GetUserId(User);

        _logger?.LogInformation(
            "Administrator {AdminId} slettet " +
            "ressurs {ResourceId} for bruker {UserId}.",
            adminId,
            deletedResourceId,
            userId
        );

        // ========================================
        // VIS BEKREFTELSE
        // ========================================

        await this.AuditAsync(
            AuditActions.ResourceDeleted,
            $"Administrator slettet ressurs #{deletedResourceId} «{deletedResourceName}» (eier {userId})"
        );

        TempData["AdminMessage"] =
            $"Ressursen «{deletedResourceName}» " +
            "ble slettet.";

        return RedirectToAction(
            nameof(UserResources),
            new { userId }
        );
    }

    // =========================================================
    // OPPRETT OFFENTLIG AKTØR
    // =========================================================

    [HttpGet]
    public IActionResult CreatePublicActor()
    {
        return View(
            new CreatePublicActorViewModel()
        );
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePublicActor(
        CreatePublicActorViewModel model
    )
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        string email =
            model.Email.Trim();

        if (
            await _userManager.FindByEmailAsync(email)
            is not null
        )
        {
            ModelState.AddModelError(
                nameof(model.Email),
                "Det finnes allerede en bruker " +
                "med denne e-posten."
            );

            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            Name = model.Name.Trim(),
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        IdentityResult result =
            await _userManager.CreateAsync(
                user,
                model.Password
            );

        if (!result.Succeeded)
        {
            foreach (IdentityError error in result.Errors)
            {
                ModelState.AddModelError(
                    string.Empty,
                    error.Description
                );
            }

            return View(model);
        }

        await _userManager.AddToRoleAsync(
            user,
            IdentitySeeder.PublicActorRole
        );

        await this.AuditAsync(
            AuditActions.UserCreated,
            $"Opprettet offentlig aktør {user.Name} ({user.Email})"
        );

        TempData["AdminMessage"] =
            $"{user.Name} ble opprettet som offentlig aktør.";

        return RedirectToAction(nameof(Users));
    }

    // =========================================================
    // ENDRE ROLLE
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(
        string userId,
        string role
    )
    {
        if (!AllowedRoles.Contains(role))
        {
            return BadRequest();
        }

        if (
            userId == _userManager.GetUserId(User)
        )
        {
            TempData["AdminError"] =
                "Du kan ikke endre din egen rolle.";

            return RedirectToAction(nameof(Users));
        }

        ApplicationUser? user =
            await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return NotFound();
        }

        IList<string> currentRoles =
            await _userManager.GetRolesAsync(user);

        await _userManager.RemoveFromRolesAsync(
            user,
            currentRoles
        );

        await _userManager.AddToRoleAsync(
            user,
            role
        );

        // Ny innlogging kreves for å bruke ny rolle.
        await _userManager.UpdateSecurityStampAsync(user);

        await this.AuditAsync(
            AuditActions.RoleChanged,
            $"{user.Name} ({user.Email}): {string.Join(", ", currentRoles)} → {role}"
        );

        TempData["AdminMessage"] =
            $"Rollen til {user.Name} ble endret.";

        return RedirectToAction(nameof(Users));
    }

    // =========================================================
    // DEAKTIVER BRUKER
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(
        string userId
    )
    {
        if (
            userId == _userManager.GetUserId(User)
        )
        {
            TempData["AdminError"] =
                "Du kan ikke deaktivere deg selv.";

            return RedirectToAction(nameof(Users));
        }

        ApplicationUser? user =
            await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return NotFound();
        }

        await _userManager.SetLockoutEnabledAsync(
            user,
            true
        );

        await _userManager.SetLockoutEndDateAsync(
            user,
            DateTimeOffset.MaxValue
        );

        // Ugyldiggjør eksisterende innlogging
        // ved neste sikkerhetsstempelkontroll.

        await _userManager.UpdateSecurityStampAsync(user);

        await this.AuditAsync(
            AuditActions.UserDeactivated,
            $"{user.Name} ({user.Email}) ble deaktivert"
        );

        TempData["AdminMessage"] =
            $"{user.Name} ble deaktivert.";

        return RedirectToAction(nameof(Users));
    }

    // =========================================================
    // REAKTIVER BRUKER
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(
        string userId
    )
    {
        ApplicationUser? user =
            await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return NotFound();
        }

        await _userManager.SetLockoutEndDateAsync(
            user,
            null
        );

        await _userManager.ResetAccessFailedCountAsync(
            user
        );

        await this.AuditAsync(
            AuditActions.UserReactivated,
            $"{user.Name} ({user.Email}) ble reaktivert"
        );

        TempData["AdminMessage"] =
            $"{user.Name} kan logge inn igjen.";

        return RedirectToAction(nameof(Users));
    }

    // =========================================================
    // AKTIVITETSLOGG (AUDIT-LOGG)
    // =========================================================

    private const int AuditPageSize = 50;

    /// <summary>
    /// Viser hvem som gjorde hva og når: innlogginger, endringer
    /// på ressurser og behov, forespørsler, rolleendringer og eksport.
    /// Loggen kan bare leses, ikke endres, fra portalen.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> AuditLog(
        // Ikke «action»: det navnet er reservert for ruten i MVC.
        string? handling,
        string? q,
        int days = 7,
        int page = 1
    )
    {
        if (_context is null)
        {
            return Problem("Databasen er ikke tilgjengelig.");
        }

        days = days is 1 or 7 or 30 or 365 ? days : 7;
        page = Math.Max(1, page);

        DateTime since = DateTime.UtcNow.AddDays(-days);

        IQueryable<AuditLogEntry> query =
            _context.AuditLog
                .AsNoTracking()
                .Where(entry => entry.Timestamp >= since);

        if (!string.IsNullOrWhiteSpace(handling))
        {
            query = query.Where(entry => entry.Action == handling);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            string search = q.Trim();

            query = query.Where(entry =>
                (entry.UserEmail != null && entry.UserEmail.Contains(search))
                || entry.Details.Contains(search));
        }

        int total = await query.CountAsync();

        List<AuditLogEntry> entries =
            await query
                .OrderByDescending(entry => entry.Timestamp)
                .ThenByDescending(entry => entry.Id)
                .Skip((page - 1) * AuditPageSize)
                .Take(AuditPageSize)
                .ToListAsync();

        // Antall per handling i perioden, til oppsummeringen øverst.
        Dictionary<string, int> counts =
            await _context.AuditLog
                .AsNoTracking()
                .Where(entry => entry.Timestamp >= since)
                .GroupBy(entry => entry.Action)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Key, item => item.Count);

        return View(new AuditLogViewModel
        {
            Entries = entries,
            Action = string.IsNullOrWhiteSpace(handling) ? null : handling,
            Query = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
            Days = days,
            Page = page,
            TotalCount = total,
            PageSize = AuditPageSize,
            ActionCounts = counts
        });
    }
}
