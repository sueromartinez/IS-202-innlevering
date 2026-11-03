using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer innlogging, registrering,
/// utlogging og profil.
///
/// ASP.NET Core Identity brukes nå for ekte brukere.
/// Session beholdes midlertidig fordi eksisterende
/// dashboard fortsatt bruker Session-data.
/// </summary>
public class AccountController : Controller
{
    private const string PublicActorEmail =
        "offentlig@beredskap.no";

    private const string ResourceProviderEmail =
        "privat@beredskap.no";

    private const string TestPassword =
        "Test123!";

    private const string PublicActorRole =
        "PublicActor";

    private const string ResourceProviderRole =
        "ResourceProvider";


    private readonly UserManager<ApplicationUser>
        _userManager;

    private readonly SignInManager<ApplicationUser>
        _signInManager;

    private readonly RoleManager<IdentityRole>
        _roleManager;


    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        RoleManager<IdentityRole> roleManager
    )
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
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


        // =========================
        // EKTE IDENTITY-BRUKER
        // =========================

        ApplicationUser? user =
            await _userManager.FindByEmailAsync(
                email
            );


        if (user is not null)
        {
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
                IList<string> roles =
                    await _userManager
                        .GetRolesAsync(user);


                string role =
                    roles.FirstOrDefault()
                    ?? ResourceProviderRole;


                CreateSession(
                    user.Email ?? email,
                    string.IsNullOrWhiteSpace(
                        user.Name
                    )
                        ? email
                        : user.Name,
                    role
                );


                return RedirectToAction(
                    "Dashboard",
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


            if (result.RequiresTwoFactor)
            {
                TempData["LoginError"] =
                    "Denne brukeren krever tofaktorautentisering. 2FA kobles til senere.";

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
        // MIDLERTIDIGE TESTBRUKERE
        // =========================

        if (
            string.Equals(
                email,
                PublicActorEmail,
                StringComparison.OrdinalIgnoreCase
            )
            &&
            model.Password == TestPassword
        )
        {
            CreateSession(
                PublicActorEmail,
                "Beredskapsbruker",
                PublicActorRole
            );


            return RedirectToAction(
                "Dashboard",
                "Home"
            );
        }


        if (
            string.Equals(
                email,
                ResourceProviderEmail,
                StringComparison.OrdinalIgnoreCase
            )
            &&
            model.Password == TestPassword
        )
        {
            CreateSession(
                ResourceProviderEmail,
                "Ressursleverandør",
                ResourceProviderRole
            );


            return RedirectToAction(
                "Dashboard",
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
        SaveRegistrationValues(model);


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


        // Nye brukere fra den åpne registreringen
        // blir kun ressursleverandører.
        if (
            !await _roleManager.RoleExistsAsync(
                ResourceProviderRole
            )
        )
        {
            IdentityResult roleResult =
                await _roleManager.CreateAsync(
                    new IdentityRole(
                        ResourceProviderRole
                    )
                );


            if (!roleResult.Succeeded)
            {
                TempData["RegisterError"] =
                    "Kunne ikke opprette brukerrollen.";

                return RedirectToAction(
                    "Index",
                    "Home"
                );
            }
        }


        var user =
            new ApplicationUser
            {
                UserName = email,
                Email = email,

                Name =
                    model.Name.Trim(),

                CreatedAt =
                    DateTime.UtcNow,

                EmailConfirmed =
                    false
            };


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


        IdentityResult addRoleResult =
            await _userManager.AddToRoleAsync(
                user,
                ResourceProviderRole
            );


        if (!addRoleResult.Succeeded)
        {
            // Ikke la en halvferdig bruker
            // bli liggende i databasen.
            await _userManager.DeleteAsync(
                user
            );


            TempData["RegisterError"] =
                "Brukeren kunne ikke kobles til riktig rolle.";


            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        // Logger den nye brukeren inn.
        await _signInManager.SignInAsync(
            user,
            isPersistent: false
        );


        // Eksisterende dashboard bruker
        // fortsatt Session.
        CreateSession(
            email,
            user.Name,
            ResourceProviderRole
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
        await _signInManager.SignOutAsync();


        CreateSession(
            PublicActorEmail,
            "Beredskapsbruker",
            PublicActorRole
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


        return View(model);
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
            return View(model);
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
    // HELPERS
    // =========================

    private void CreateSession(
        string email,
        string name,
        string role
    )
    {
        HttpContext.Session.Clear();


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