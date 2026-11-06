
using System.Text;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;

namespace FirstWebAppInDocker.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IWebHostEnvironment? _environment;
    private readonly IEmailCodeSender? _emailSender;

    // Brukes bare til å følge registreringsstatus
    // mens brukeren venter på e-postbekreftelse.
    private const string PendingRegistrationUserIdKey =
        "PendingRegistrationUserId";

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IWebHostEnvironment? environment = null,
        IEmailCodeSender? emailSender = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _environment = environment;
        _emailSender = emailSender;
    }

    // ========================================
    // LOGIN
    // ========================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["LoginError"] =
                "Fyll inn gyldig e-post og passord.";

            return RedirectToAction("Index", "Home");
        }

        string email = model.Email.Trim();

        ApplicationUser? user =
            await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            TempData["LoginError"] =
                "Feil e-post eller passord.";

            return RedirectToAction("Index", "Home");
        }

        // E-postadressen må være bekreftet
        // før vanlig innlogging.

        if (!user.EmailConfirmed)
        {
            TempData["LoginError"] =
                "Du må bekrefte e-postadressen din " +
                "før du kan logge inn. " +
                "Sjekk innboksen din.";

            return RedirectToAction("Index", "Home");
        }

        var result =
            await _signInManager.PasswordSignInAsync(
                user,
                model.Password,
                isPersistent: false,
                lockoutOnFailure: true
            );

        if (result.Succeeded)
        {
            string role =
                await GetPrimaryRoleAsync(user);

            CreateSession(user, role);

            return RedirectToAction(
                "Dashboard",
                "Home"
            );
        }

        // Passordet er godkjent, men
        // tofaktorinnlogging gjenstår.

        if (result.RequiresTwoFactor)
        {
            return RedirectToAction(
                "Login",
                "TwoFactor"
            );
        }

        if (result.IsLockedOut)
        {
            TempData["LoginError"] =
                "Brukeren er midlertidig låst " +
                "etter for mange feilaktige " +
                "innloggingsforsøk.";

            return RedirectToAction(
                "Index",
                "Home"
            );
        }

        TempData["LoginError"] =
            "Feil e-post eller passord.";

        return RedirectToAction("Index", "Home");
    }

    // ========================================
    // REGISTER
    // ========================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        RegisterViewModel model)
    {
        SaveRegistrationValues(model);

        if (!ModelState.IsValid)
        {
            TempData["RegisterError"] =
                "Kontroller at alle feltene er " +
                "fylt ut riktig, og at " +
                "passordene er like.";

            return RedirectToAction("Index", "Home");
        }

        string email = model.Email.Trim();

        ApplicationUser? existingUser =
            await _userManager.FindByEmailAsync(email);

        if (existingUser is not null)
        {
            TempData["RegisterError"] =
                "Det finnes allerede en bruker " +
                "med denne e-postadressen.";

            return RedirectToAction("Index", "Home");
        }

        // ========================================
        // OPPRETT BRUKER
        // ========================================

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            Name = model.Name.Trim(),
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = false
        };

        IdentityResult createResult =
            await _userManager.CreateAsync(
                user,
                model.Password
            );

        if (!createResult.Succeeded)
        {
            string errors = string.Join(
                " ",
                createResult.Errors.Select(
                    error => error.Description
                )
            );

            TempData["RegisterError"] = errors;

            return RedirectToAction("Index", "Home");
        }

        // ========================================
        // TILDEL RESOURCEPROVIDER-ROLLE
        // ========================================

        IdentityResult roleResult =
            await _userManager.AddToRoleAsync(
                user,
                IdentitySeeder.ResourceProviderRole
            );

        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);

            string errors = string.Join(
                " ",
                roleResult.Errors.Select(
                    error => error.Description
                )
            );

            TempData["RegisterError"] =
                "Brukeren kunne ikke få riktig rolle. " +
                errors;

            return RedirectToAction("Index", "Home");
        }

        // ========================================
        // KONTROLLER E-POSTTJENESTEN
        // ========================================

        if (_emailSender is null)
        {
            TempData["RegisterError"] =
                "Kontoen ble opprettet, men " +
                "e-posttjenesten er ikke tilgjengelig. " +
                "Kontakt administrator.";

            return RedirectToAction("Index", "Home");
        }

        // ========================================
        // GENERER BEKREFTELSESTOKEN
        // ========================================

        string confirmationToken =
            await _userManager.GenerateEmailConfirmationTokenAsync(
                user
            );

        if (string.IsNullOrWhiteSpace(confirmationToken))
        {
            TempData["RegisterError"] =
                "Kontoen ble opprettet, men " +
                "bekreftelseslenken kunne ikke lages.";

            return RedirectToAction("Index", "Home");
        }

        string encodedToken =
            WebEncoders.Base64UrlEncode(
                Encoding.UTF8.GetBytes(
                    confirmationToken
                )
            );

        // ========================================
        // GENERER BEKREFTELSESLENKE
        // ========================================

        string? confirmationUrl = Url.Action(
            action: "Confirm",
            controller: "EmailVerification",
            values: new
            {
                userId = user.Id,
                code = encodedToken
            },
            protocol: Request.Scheme
        );

        if (string.IsNullOrWhiteSpace(confirmationUrl))
        {
            TempData["RegisterError"] =
                "Kontoen ble opprettet, men " +
                "bekreftelseslenken kunne ikke lages.";

            return RedirectToAction("Index", "Home");
        }

        // ========================================
        // SEND BEKREFTELSESMAIL
        // ========================================

        try
        {
            await _emailSender.SendConfirmationAsync(
                email,
                confirmationUrl
            );
        }
        catch (Exception)
        {
            TempData["RegisterError"] =
                "Kontoen ble opprettet, men vi " +
                "klarte ikke å sende bekreftelsesmail. " +
                "Kontakt administrator.";

            return RedirectToAction("Index", "Home");
        }

        // ========================================
        // NYTT: KLARGJØR REGISTRERINGSPOPUP
        // ========================================
        //
        // Brukeren skal IKKE logges inn
        // automatisk.
        //
        // Vi lagrer bare Identity-ID til
        // registreringen som venter på
        // bekreftelse.
        //
        // Dette er ikke en innloggingssesjon.

        HttpContext.Session.Clear();

        HttpContext.Session.SetString(
            PendingRegistrationUserIdKey,
            user.Id
        );

        // Viser nå Razor-siden med popup
        // i stedet for ren tekst.

        return RedirectToAction(
            nameof(RegistrationPending)
        );
    }

    // ========================================
    // NYTT: REGISTRERING VENTER
    // ========================================

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> RegistrationPending()
    {
        string? pendingUserId =
            HttpContext.Session.GetString(
                PendingRegistrationUserIdKey
            );

        // Popupen skal bare vise statusen
        // til registreringen fra denne sesjonen.

        if (string.IsNullOrWhiteSpace(pendingUserId))
        {
            return RedirectToAction(
                "Index",
                "Home"
            );
        }

        ApplicationUser? user =
            await _userManager.FindByIdAsync(
                pendingUserId
            );

        if (user is null)
        {
            HttpContext.Session.Remove(
                PendingRegistrationUserIdKey
            );

            return RedirectToAction(
                "Index",
                "Home"
            );
        }

        // Vi viser "fullført" først når
        // e-posten er bekreftet OG tofaktor
        // er aktivert.

        bool emailConfirmed =
            await _userManager.IsEmailConfirmedAsync(
                user
            );

        bool twoFactorEnabled =
            await _userManager.GetTwoFactorEnabledAsync(
                user
            );

        ViewData["RegistrationComplete"] =
            emailConfirmed && twoFactorEnabled;

        return View();
    }

    // ========================================
    // DEMO PUBLIC ACTOR
    // ========================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoPublicActorLogin()
    {
        if (_environment?.IsDevelopment() != true)
        {
            return NotFound();
        }

        ApplicationUser? publicActor =
            await _userManager.FindByEmailAsync(
                IdentitySeeder.PublicActorEmail
            );

        if (publicActor is null)
        {
            TempData["LoginError"] =
                "Offentlig testbruker finnes " +
                "ikke i databasen.";

            return RedirectToAction("Index", "Home");
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

            return RedirectToAction("Index", "Home");
        }

        // Demoinnlogging skal ikke omgå
        // en konto med aktivert tofaktor.

        bool twoFactorEnabled =
            await _userManager.GetTwoFactorEnabledAsync(
                publicActor
            );

        if (twoFactorEnabled)
        {
            TempData["LoginError"] =
                "Demoinnlogging kan ikke brukes " +
                "når tofaktorautentisering er aktivert. " +
                "Logg inn med e-post og passord.";

            return RedirectToAction("Index", "Home");
        }

        await _signInManager.SignOutAsync();

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

    // ========================================
    // LOGOUT
    // ========================================

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

    // ========================================
    // ACCESS DENIED
    // ========================================

    [HttpGet]
    public IActionResult AccessDenied()
    {
        TempData["LoginError"] =
            "Du har ikke tilgang til denne " +
            "delen av Beredskapsportalen.";

        return RedirectToAction(
            "Index",
            "Home"
        );
    }

    // ========================================
    // PROFILE - GET
    // ========================================

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        ApplicationUser? user =
            await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        var phone =
            SplitPhoneNumber(user.PhoneNumber);

        var model = new ProfileViewModel
        {
            Name = user.Name,

            Email =
                user.Email
                ?? user.UserName
                ?? string.Empty,

            PhoneCountryCode = phone.CountryCode,
            PhoneNumber = phone.Number
        };

        return View(model);
    }

    // ========================================
    // PROFILE - POST
    // ========================================

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(
        ProfileViewModel model)
    {
        ApplicationUser? user =
            await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        string newEmail = model.Email.Trim();

        string currentEmail =
            user.Email
            ?? user.UserName
            ?? string.Empty;

        bool emailChanged =
            !string.Equals(
                currentEmail,
                newEmail,
                StringComparison.OrdinalIgnoreCase
            );

        // ========================================
        // VALIDER NY E-POSTADRESSE
        // ========================================

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
                    "Det finnes allerede en bruker " +
                    "med denne e-postadressen."
                );

                return View(model);
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

                return View(model);
            }

            if (_emailSender is null)
            {
                ModelState.AddModelError(
                    nameof(ProfileViewModel.Email),
                    "E-posttjenesten er ikke tilgjengelig. " +
                    "E-postadressen er ikke endret."
                );

                return View(model);
            }
        }

        // ========================================
        // OPPDATER NAVN OG TELEFON
        // ========================================
        //
        // Vi endrer IKKE e-postadressen her.
        //
        // Den gamle adressen fungerer til
        // brukeren har bekreftet den nye.

        user.Name = model.Name.Trim();

        user.PhoneNumber =
            BuildPhoneNumber(
                model.PhoneCountryCode,
                model.PhoneNumber
            );

        IdentityResult updateResult =
            await _userManager.UpdateAsync(user);

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

            return View(model);
        }

        await _signInManager.RefreshSignInAsync(
            user
        );

        // ========================================
        // OPPDATER SESSION
        // ========================================

        HttpContext.Session.SetString(
            "UserName",
            user.Name
        );

        HttpContext.Session.SetString(
            "UserEmail",
            currentEmail
        );

        HttpContext.Session.SetString(
            "ContactEmail",
            currentEmail
        );

        HttpContext.Session.SetString(
            "PhoneCountryCode",
            model.PhoneCountryCode
        );

        if (string.IsNullOrWhiteSpace(model.PhoneNumber))
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

        // ========================================
        // INGEN E-POSTENDRING
        // ========================================

        if (!emailChanged)
        {
            TempData["ProfileSuccess"] =
                "Profilen ble oppdatert.";

            return RedirectToAction(
                nameof(Profile)
            );
        }

        // ========================================
        // GENERER TOKEN FOR NY E-POST
        // ========================================

        string changeToken =
            await _userManager.GenerateChangeEmailTokenAsync(
                user,
                newEmail
            );

        if (string.IsNullOrWhiteSpace(changeToken))
        {
            ModelState.AddModelError(
                nameof(ProfileViewModel.Email),
                "Navn og telefon er lagret, men " +
                "vi kunne ikke lage en bekreftelseslenke. " +
                "Den gamle e-postadressen er fortsatt aktiv."
            );

            return View(model);
        }

        string encodedToken =
            WebEncoders.Base64UrlEncode(
                Encoding.UTF8.GetBytes(
                    changeToken
                )
            );

        // ========================================
        // GENERER BEKREFTELSESLENKE
        // ========================================

        string? confirmationUrl = Url.Action(
            action: "ConfirmChange",
            controller: "EmailVerification",
            values: new
            {
                userId = user.Id,
                newEmail = newEmail,
                code = encodedToken
            },
            protocol: Request.Scheme
        );

        if (string.IsNullOrWhiteSpace(confirmationUrl))
        {
            ModelState.AddModelError(
                nameof(ProfileViewModel.Email),
                "Navn og telefon er lagret, men " +
                "bekreftelseslenken kunne ikke lages. " +
                "Den gamle e-postadressen er fortsatt aktiv."
            );

            return View(model);
        }

        // ========================================
        // SEND BEKREFTELSE TIL NY E-POST
        // ========================================

        try
        {
            await _emailSender!.SendConfirmationAsync(
                newEmail,
                confirmationUrl
            );
        }
        catch (Exception)
        {
            ModelState.AddModelError(
                nameof(ProfileViewModel.Email),
                "Navn og telefon er lagret, men " +
                "vi klarte ikke å sende bekreftelseslenken. " +
                "Den gamle e-postadressen fungerer fortsatt. " +
                "Prøv igjen senere."
            );

            return View(model);
        }

        TempData["ProfileSuccess"] =
            "Profilopplysningene er lagret. " +
            "Vi har sendt en bekreftelseslenke " +
            "til den nye e-postadressen. " +
            "Den gamle adressen fungerer " +
            "frem til den nye er bekreftet.";

        return RedirectToAction(
            nameof(Profile)
        );
    }

    // ========================================
    // PHONE HELPERS
    // ========================================

    private static string? BuildPhoneNumber(
        string countryCode,
        string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        string normalizedCountryCode =
            countryCode.Trim();

        string normalizedPhoneNumber =
            phoneNumber
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty);

        return normalizedCountryCode
            + normalizedPhoneNumber;
    }

    private static (
        string CountryCode,
        string? Number
    ) SplitPhoneNumber(
        string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return ("+47", null);
        }

        string[] supportedCountryCodes =
        {
            "+47",
            "+45",
            "+46"
        };

        foreach (string countryCode in supportedCountryCodes)
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
                    phoneNumber[countryCode.Length..]
                );
            }
        }

        return ("+47", phoneNumber);
    }

    // ========================================
    // ROLE HELPER
    // ========================================

    private async Task<string> GetPrimaryRoleAsync(
        ApplicationUser user)
    {
        IList<string> roles =
            await _userManager.GetRolesAsync(user);

        if (roles.Contains(IdentitySeeder.AdminRole))
        {
            return IdentitySeeder.AdminRole;
        }

        if (roles.Contains(IdentitySeeder.PublicActorRole))
        {
            return IdentitySeeder.PublicActorRole;
        }

        return IdentitySeeder.ResourceProviderRole;
    }

    // ========================================
    // SESSION HELPER
    // ========================================

    private void CreateSession(
        ApplicationUser user,
        string role)
    {
        HttpContext.Session.Clear();

        string email =
            user.Email
            ?? user.UserName
            ?? string.Empty;

        string name =
            string.IsNullOrWhiteSpace(user.Name)
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
            SplitPhoneNumber(user.PhoneNumber);

        HttpContext.Session.SetString(
            "PhoneCountryCode",
            phone.CountryCode
        );

        if (string.IsNullOrWhiteSpace(phone.Number))
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

    // ========================================
    // REGISTRATION HELPER
    // ========================================

    private void SaveRegistrationValues(
        RegisterViewModel model)
    {
        TempData["RegisterName"] =
            model.Name?.Trim()
            ?? string.Empty;

        TempData["RegisterEmail"] =
            model.Email?.Trim()
            ?? string.Empty;
    }
}
