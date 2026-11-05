using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Brukeradministrasjon. Kun for Admin.
///
/// Admin er systemforvalter, ikke kriseleder:
/// - oppretter offentlige aktører (de kan ikke registrere seg selv)
/// - endrer roller
/// - deaktiverer og reaktiverer kontoer
///
/// Brukere slettes ikke, men deaktiveres. Da beholdes historikken
/// over behov, ressurser og tildelinger.
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


    public AdminController(
        UserManager<ApplicationUser> userManager
    )
    {
        _userManager = userManager;
    }


    // =========================================================
    // BRUKEROVERSIKT
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Users()
    {
        string? currentUserId = _userManager.GetUserId(User);

        List<ApplicationUser> users =
            await _userManager.Users
                .AsNoTracking()
                .OrderBy(user => user.Name)
                .ToListAsync();

        var model = new List<AdminUserViewModel>();

        foreach (ApplicationUser user in users)
        {
            IList<string> roles = await _userManager.GetRolesAsync(user);

            bool isLocked =
                user.LockoutEnd.HasValue
                && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

            // Deaktivering bruker en lockout langt frem i tid,
            // mens vanlig lockout etter feil passord varer i minutter.
            bool isDeactivated =
                isLocked
                && user.LockoutEnd!.Value > DateTimeOffset.UtcNow.AddYears(50);

            model.Add(new AdminUserViewModel
            {
                Id = user.Id,
                Name = string.IsNullOrWhiteSpace(user.Name) ? "(uten navn)" : user.Name,
                Email = user.Email ?? string.Empty,
                Role = roles.FirstOrDefault() ?? "Ingen rolle",
                IsDeactivated = isDeactivated,
                IsTemporarilyLocked = isLocked && !isDeactivated,
                IsCurrentUser = user.Id == currentUserId,
                CreatedAt = user.CreatedAt
            });
        }

        return View(model);
    }


    // =========================================================
    // OPPRETT OFFENTLIG AKTØR
    // =========================================================

    [HttpGet]
    public IActionResult CreatePublicActor()
    {
        return View(new CreatePublicActorViewModel());
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

        string email = model.Email.Trim();

        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(
                nameof(model.Email),
                "Det finnes allerede en bruker med denne e-posten."
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
            await _userManager.CreateAsync(user, model.Password);

        if (!result.Succeeded)
        {
            foreach (IdentityError error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await _userManager.AddToRoleAsync(user, IdentitySeeder.PublicActorRole);

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

        if (userId == _userManager.GetUserId(User))
        {
            TempData["AdminError"] = "Du kan ikke endre din egen rolle.";
            return RedirectToAction(nameof(Users));
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return NotFound();
        }

        IList<string> currentRoles = await _userManager.GetRolesAsync(user);

        await _userManager.RemoveFromRolesAsync(user, currentRoles);
        await _userManager.AddToRoleAsync(user, role);

        // Tvinger ny innlogging slik at den nye rollen tas i bruk.
        await _userManager.UpdateSecurityStampAsync(user);

        TempData["AdminMessage"] = $"Rollen til {user.Name} ble endret.";

        return RedirectToAction(nameof(Users));
    }


    // =========================================================
    // DEAKTIVER / REAKTIVER
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(string userId)
    {
        if (userId == _userManager.GetUserId(User))
        {
            TempData["AdminError"] = "Du kan ikke deaktivere deg selv.";
            return RedirectToAction(nameof(Users));
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return NotFound();
        }

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        // Logger brukeren ut ved neste validering av innloggingen.
        await _userManager.UpdateSecurityStampAsync(user);

        TempData["AdminMessage"] = $"{user.Name} ble deaktivert.";

        return RedirectToAction(nameof(Users));
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(string userId)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return NotFound();
        }

        // Fjerner både deaktivering og midlertidig lås
        // etter for mange feil passord.
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);

        TempData["AdminMessage"] = $"{user.Name} kan logge inn igjen.";

        return RedirectToAction(nameof(Users));
    }
}
