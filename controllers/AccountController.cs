using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer registrering, innlogging,
/// utlogging og profil.
///
/// ASP.NET Core Identity håndterer:
/// - brukere
/// - passord
/// - roller
/// - autentisering
///
/// Session beholdes midlertidig fordi deler av
/// eksisterende dashboard fortsatt bruker den.
/// </summary>
public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser>
        _userManager;

    private readonly SignInManager<ApplicationUser>
        _signInManager;


    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager
    )
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }


    // =========================
    // LOGIN
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model
    )
    {
        if (!ModelState.IsValid)
        {
            TempData["LoginError"] =
                "Fyll inn gyldig e-post og passord.";

            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        string email =
            model.Email.Trim();


        ApplicationUser? user =
            await _userManager.FindByEmailAsync(
                email
            );


        if (user is null)
        {
            TempData["LoginError"] =
                "Feil e-post eller passord.";

            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        var result =
            await _signInManager
                .PasswordSignInAsync(
                    user,
                    model.Password,
                    isPersistent: false,
                    lockoutOnFailure: true
                );


        if (result.Succeeded)
        {
            string role =
                await GetPrimaryRoleAsync(
                    user
                );


            CreateSession(
                user,
                role
            );


            return RedirectToAction(
                "Dashboard",
                "Home"
            );
        }


        if (result.RequiresTwoFactor)
        {
            TempData["LoginError"] =
                "Denne brukeren krever tofaktorautentisering.";

            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        if (result.IsLockedOut)
        {
            TempData["LoginError"] =
                "Brukeren er midlertidig låst etter for mange feilaktige innloggingsforsøk.";

            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        TempData["LoginError"] =
            "Feil e-post eller passord.";


        return RedirectToAction(
            "Index",
            "Home"
        );
    }


    // =========================
    // REGISTER
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        RegisterViewModel model
    )
    {
        SaveRegistrationValues(
            model
        );


        if (!ModelState.IsValid)
        {
            TempData["RegisterError"] =
                "Kontroller at alle feltene er fylt ut riktig, og at passordene er like.";

            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        string email =
            model.Email.Trim();


        ApplicationUser? existingUser =
            await _userManager.FindByEmailAsync(
                email
            );


        if (existingUser is not null)
        {
            TempData["RegisterError"] =
                "Det finnes allerede en bruker med denne e-postadressen.";

            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        var user =
            new ApplicationUser
            {
                UserName =
                    email,

                Email =
                    email,

                Name =
                    model.Name.Trim(),

                CreatedAt =
                    DateTime.UtcNow,

                EmailConfirmed =
                    false
            };


        // =========================
        // CREATE USER
        // =========================

        IdentityResult createResult =
            await _userManager.CreateAsync(
                user,
                model.Password
            );


        if (!createResult.Succeeded)
        {
            string errors =
                string.Join(
                    " ",
                    createResult.Errors.Select(
                        error =>
                            error.Description
                    )
                );


            TempData["RegisterError"] =
                errors;


            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        // =========================
        // ASSIGN ROLE
        // =========================
        //
        // Åpen registrering gir KUN
        // ResourceProvider.
        //
        // PublicActor og Admin kan ikke
        // velges av brukeren selv.
        //

        IdentityResult roleResult =
            await _userManager.AddToRoleAsync(
                user,
                IdentitySeeder.ResourceProviderRole
            );


        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(
                user
            );


            string errors =
                string.Join(
                    " ",
                    roleResult.Errors.Select(
                        error =>
                            error.Description
                    )
                );


            TempData["RegisterError"] =
                $"Brukeren kunne ikke få riktig rolle. {errors}";


            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        // =========================
        // SIGN IN
        // =========================

        await _signInManager.SignInAsync(
            user,
            isPersistent: false
        );


        CreateSession(
            user,
            IdentitySeeder.ResourceProviderRole
        );


        return RedirectToAction(
            "Dashboard",
            "Home"
        );
    }


    // =========================
    // DEMO PUBLIC ACTOR
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult>
        DemoPublicActorLogin()
    {
        // Logg ut eventuell eksisterende
        // Identity-bruker først.
        await _signInManager.SignOutAsync();


        ApplicationUser? publicActor =
            await _userManager.FindByEmailAsync(
                IdentitySeeder.PublicActorEmail
            );


        if (publicActor is null)
        {
            TempData["LoginError"] =
                "Offentlig testbruker finnes ikke i databasen.";

            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        bool hasCorrectRole =
            await _userManager.IsInRoleAsync(
                publicActor,
                IdentitySeeder.PublicActorRole
            );


        if (!hasCorrectRole)
        {
            TempData["LoginError"] =
                "Offentlig testbruker mangler riktig rolle.";

            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        // Dette er en midlertidig demo-knapp.
        // Vi logger brukeren inn direkte uten
        // passord fordi knappen kun representerer
        // prosjektets testbruker.
        await _signInManager.SignInAsync(
            publicActor,
            isPersistent: false
        );


        CreateSession(
            publicActor,
            IdentitySeeder.PublicActorRole
        );


        return RedirectToAction(
            "Dashboard",
            "Home"
        );
    }


    // =========================
    // LOGOUT
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();

        HttpContext.Session.Clear();


        return RedirectToAction(
            "Index",
            "Home"
        );
    }


    // =========================
    // ACCESS DENIED
    // =========================

    [HttpGet]
    public IActionResult AccessDenied()
    {
        TempData["LoginError"] =
            "Du har ikke tilgang til denne delen av Beredskapsportalen.";


        return RedirectToAction(
            "Index",
            "Home"
        );
    }


    // =========================
    // PROFILE
    // =========================

    [HttpGet]
    public IActionResult Profile()
    {
        string? userEmail =
            HttpContext.Session.GetString(
                "UserEmail"
            );


        if (
            string.IsNullOrWhiteSpace(
                userEmail
            )
        )
        {
            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        var model =
            new ProfileViewModel
            {
                Name =
                    HttpContext.Session.GetString(
                        "UserName"
                    )
                    ?? string.Empty,

                Email =
                    HttpContext.Session.GetString(
                        "ContactEmail"
                    )
                    ?? userEmail,

                PhoneCountryCode =
                    HttpContext.Session.GetString(
                        "PhoneCountryCode"
                    )
                    ?? "+47",

                PhoneNumber =
                    HttpContext.Session.GetString(
                        "PhoneNumber"
                    )
            };


        return View(
            model
        );
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Profile(
        ProfileViewModel model
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                HttpContext.Session.GetString(
                    "UserEmail"
                )
            )
        )
        {
            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        if (!ModelState.IsValid)
        {
            return View(
                model
            );
        }


        HttpContext.Session.SetString(
            "UserName",
            model.Name.Trim()
        );


        HttpContext.Session.SetString(
            "ContactEmail",
            model.Email.Trim()
        );


        HttpContext.Session.SetString(
            "PhoneCountryCode",
            model.PhoneCountryCode
        );


        if (
            string.IsNullOrWhiteSpace(
                model.PhoneNumber
            )
        )
        {
            HttpContext.Session.Remove(
                "PhoneNumber"
            );
        }
        else
        {
            HttpContext.Session.SetString(
                "PhoneNumber",
                model.PhoneNumber.Trim()
            );
        }


        TempData["ProfileSuccess"] =
            "Profilen ble oppdatert.";


        return RedirectToAction(
            nameof(Profile)
        );
    }


    // =========================
    // ROLE HELPER
    // =========================

    private async Task<string>
        GetPrimaryRoleAsync(
            ApplicationUser user
        )
    {
        IList<string> roles =
            await _userManager
                .GetRolesAsync(
                    user
                );


        // Prioriter høyeste tilgang
        // dersom en bruker senere har
        // flere roller.
        if (
            roles.Contains(
                IdentitySeeder.AdminRole
            )
        )
        {
            return IdentitySeeder.AdminRole;
        }


        if (
            roles.Contains(
                IdentitySeeder.PublicActorRole
            )
        )
        {
            return IdentitySeeder.PublicActorRole;
        }


        return IdentitySeeder.ResourceProviderRole;
    }


    // =========================
    // SESSION HELPER
    // =========================

    private void CreateSession(
        ApplicationUser user,
        string role
    )
    {
        HttpContext.Session.Clear();


        string email =
            user.Email
            ?? user.UserName
            ?? string.Empty;


        string name =
            string.IsNullOrWhiteSpace(
                user.Name
            )
                ? email
                : user.Name;


        HttpContext.Session.SetString(
            "UserEmail",
            email
        );


        HttpContext.Session.SetString(
            "UserName",
            name
        );


        HttpContext.Session.SetString(
            "UserRole",
            role
        );


        HttpContext.Session.SetString(
            "ContactEmail",
            email
        );


        HttpContext.Session.SetString(
            "PhoneCountryCode",
            "+47"
        );
    }


    // =========================
    // REGISTRATION HELPER
    // =========================

    private void SaveRegistrationValues(
        RegisterViewModel model
    )
    {
        TempData["RegisterName"] =
            model.Name?.Trim()
            ?? string.Empty;


        TempData["RegisterEmail"] =
            model.Email?.Trim()
            ?? string.Empty;
    }
}