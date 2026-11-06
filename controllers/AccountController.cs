using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
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
/// Identity er sannhetskilden for brukerdata.
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

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        ApplicationUser? user =
            await _userManager.GetUserAsync(
                User
            );


        if (user is null)
        {
            return Challenge();
        }


        var phone =
            SplitPhoneNumber(
                user.PhoneNumber
            );


        var model =
            new ProfileViewModel
            {
                Name =
                    user.Name,

                Email =
                    user.Email
                    ?? user.UserName
                    ?? string.Empty,

                PhoneCountryCode =
                    phone.CountryCode,

                PhoneNumber =
                    phone.Number
            };


        return View(
            model
        );
    }


    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(
        ProfileViewModel model
    )
    {
        ApplicationUser? user =
            await _userManager.GetUserAsync(
                User
            );


        if (user is null)
        {
            return Challenge();
        }


        if (!ModelState.IsValid)
        {
            return View(
                model
            );
        }


        string newEmail =
            model.Email.Trim();


        string oldEmail =
            user.Email
            ?? user.UserName
            ?? string.Empty;


        bool emailChanged =
            !string.Equals(
                oldEmail,
                newEmail,
                StringComparison.OrdinalIgnoreCase
            );


        // =========================
        // CHECK EMAIL
        // =========================

        if (emailChanged)
        {
            ApplicationUser? userWithEmail =
                await _userManager.FindByEmailAsync(
                    newEmail
                );


            if (
                userWithEmail is not null
                &&
                userWithEmail.Id != user.Id
            )
            {
                ModelState.AddModelError(
                    nameof(ProfileViewModel.Email),
                    "Det finnes allerede en bruker med denne e-postadressen."
                );


                return View(
                    model
                );
            }


            ApplicationUser? userWithUserName =
                await _userManager.FindByNameAsync(
                    newEmail
                );


            if (
                userWithUserName is not null
                &&
                userWithUserName.Id != user.Id
            )
            {
                ModelState.AddModelError(
                    nameof(ProfileViewModel.Email),
                    "Denne e-postadressen kan ikke brukes."
                );


                return View(
                    model
                );
            }
        }


        // =========================
        // UPDATE USER
        // =========================

        user.Name =
            model.Name.Trim();


        user.PhoneNumber =
            BuildPhoneNumber(
                model.PhoneCountryCode,
                model.PhoneNumber
            );


        if (emailChanged)
        {
            // E-post brukes også som brukernavn
            // i denne løsningen.
            //
            // Brukerens Id endres IKKE.

            user.Email =
                newEmail;

            user.UserName =
                newEmail;

            // Hvis vi senere legger inn
            // e-postbekreftelse, må den nye
            // adressen bekreftes på nytt.
            user.EmailConfirmed =
                false;
        }


        IdentityResult updateResult =
            await _userManager.UpdateAsync(
                user
            );


        if (!updateResult.Succeeded)
        {
            foreach (
                IdentityError error
                in updateResult.Errors
            )
            {
                ModelState.AddModelError(
                    string.Empty,
                    error.Description
                );
            }


            return View(
                model
            );
        }


        // Oppdater innloggingscookien slik
        // at Identity umiddelbart kjenner
        // den nye brukerinformasjonen.
        await _signInManager.RefreshSignInAsync(
            user
        );


        // =========================
        // SESSION COMPATIBILITY
        // =========================
        //
        // Session beholdes midlertidig fordi
        // eldre dashboardkode fortsatt bruker den.
        //
        // Identity er sannhetskilden.
        //

        HttpContext.Session.SetString(
            "UserName",
            user.Name
        );


        HttpContext.Session.SetString(
            "UserEmail",
            newEmail
        );


        HttpContext.Session.SetString(
            "ContactEmail",
            newEmail
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
    // PHONE HELPERS
    // =========================

    private static string? BuildPhoneNumber(
        string countryCode,
        string? phoneNumber
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                phoneNumber
            )
        )
        {
            return null;
        }


        string normalizedCountryCode =
            countryCode.Trim();


        string normalizedPhoneNumber =
            phoneNumber
                .Replace(
                    " ",
                    string.Empty
                )
                .Replace(
                    "-",
                    string.Empty
                );


        return
            normalizedCountryCode
            + normalizedPhoneNumber;
    }


    private static (
        string CountryCode,
        string? Number
    ) SplitPhoneNumber(
        string? phoneNumber
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                phoneNumber
            )
        )
        {
            return (
                "+47",
                null
            );
        }


        string[] supportedCountryCodes =
        {
            "+47",
            "+45",
            "+46"
        };


        foreach (
            string countryCode
            in supportedCountryCodes
        )
        {
            if (
                phoneNumber.StartsWith(
                    countryCode,
                    StringComparison.Ordinal
                )
            )
            {
                return (
                    countryCode,
                    phoneNumber[
                        countryCode.Length..
                    ]
                );
            }
        }


        return (
            "+47",
            phoneNumber
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


        var phone =
            SplitPhoneNumber(
                user.PhoneNumber
            );


        HttpContext.Session.SetString(
            "PhoneCountryCode",
            phone.CountryCode
        );


        if (
            string.IsNullOrWhiteSpace(
                phone.Number
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
                phone.Number
            );
        }
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