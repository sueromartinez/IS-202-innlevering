
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
    private readonly ILogger<AccountController>? _logger;

    private const string PendingRegistrationUserIdKey =
        "PendingRegistrationUserId";

    private const string PendingTwoFactorRememberMeKey =
        "PendingTwoFactorRememberMe";

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IWebHostEnvironment? environment = null,
        IEmailCodeSender? emailSender = null,
        ILogger<AccountController>? logger = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _environment = environment;
        _emailSender = emailSender;
        _logger = logger;
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
            await this.AuditAsync(
                AuditActions.LoginFailed,
                "Ukjent e-postadresse",
                userEmailOverride: email);

            TempData["LoginError"] =
                "Feil e-post eller passord.";

            return RedirectToAction("Index", "Home");
        }

        if (!user.EmailConfirmed)
        {
            TempData["LoginError"] =
                "Du må bekrefte e-postadressen din " +
                "før du kan logge inn. " +
                "Sjekk innboksen din.";

            return RedirectToAction("Index", "Home");
        }

        HttpContext.Session.Remove(
            PendingTwoFactorRememberMeKey);

        var result =
            await _signInManager.PasswordSignInAsync(
                user,
                model.Password,
                isPersistent: model.RememberMe,
                lockoutOnFailure: true);

        if (result.Succeeded)
        {
            string role =
                await GetPrimaryRoleAsync(user);

            CreateSession(user, role);

            await this.AuditAsync(
                AuditActions.Login,
                $"Innlogging med passord ({role})",
                user.Id,
                user.Email);

            return RedirectToAction(
                "Dashboard",
                "Home");
        }

        if (result.RequiresTwoFactor)
        {
            HttpContext.Session.SetString(
                PendingTwoFactorRememberMeKey,
                model.RememberMe ? "true" : "false");

            return RedirectToAction(
                "Login",
                "TwoFactor");
        }

        if (result.IsLockedOut)
        {
            await this.AuditAsync(
                AuditActions.LoginFailed,
                "Kontoen er låst etter for mange forsøk",
                user.Id,
                user.Email);

            TempData["LoginError"] =
                "Brukeren er midlertidig låst " +
                "etter for mange feilaktige " +
                "innloggingsforsøk.";

            return RedirectToAction(
                "Index",
                "Home");
        }

        await this.AuditAsync(
            AuditActions.LoginFailed,
            "Feil passord",
            user.Id,
            user.Email);

        TempData["LoginError"] =
            "Feil e-post eller passord.";

        return RedirectToAction("Index", "Home");
    }

    // ========================================
    // GLEMT PASSORD - GET
    // ========================================

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult ForgotPassword()
    {
        return View(
            new ForgotPasswordViewModel());
    }

    // ========================================
    // GLEMT PASSORD - POST
    // ========================================

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        string email = model.Email.Trim();

        ApplicationUser? user =
            await _userManager.FindByEmailAsync(email);

        // Samme svar uansett om e-postadressen finnes.

        if (user is null)
        {
            return RedirectToAction(
                nameof(ForgotPasswordConfirmation));
        }

        if (!await _userManager.IsEmailConfirmedAsync(user))
        {
            return RedirectToAction(
                nameof(ForgotPasswordConfirmation));
        }

        if (_emailSender is null)
        {
            _logger?.LogWarning(
                "E-posttjenesten er ikke tilgjengelig " +
                "for tilbakestilling av passord.");

            return RedirectToAction(
                nameof(ForgotPasswordConfirmation));
        }

        string token =
            await _userManager.GeneratePasswordResetTokenAsync(
                user);

        string encodedToken =
            WebEncoders.Base64UrlEncode(
                Encoding.UTF8.GetBytes(token));

        string? resetUrl = Url.Action(
            action: nameof(ResetPassword),
            controller: "Account",
            values: new
            {
                userId = user.Id,
                code = encodedToken
            },
            protocol: Request.Scheme);

        if (string.IsNullOrWhiteSpace(resetUrl))
        {
            _logger?.LogWarning(
                "Kunne ikke generere lenke for " +
                "tilbakestilling av passord.");

            return RedirectToAction(
                nameof(ForgotPasswordConfirmation));
        }

        try
        {
            await _emailSender.SendPasswordResetAsync(
                email,
                resetUrl);
        }
        catch (Exception exception)
        {
            _logger?.LogError(
                exception,
                "Kunne ikke sende passordtilbakestilling " +
                "for bruker {UserId}.",
                user.Id);
        }

        return RedirectToAction(
            nameof(ForgotPasswordConfirmation));
    }

    // ========================================
    // BEKREFTELSE ETTER FORESPØRSEL
    // ========================================

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }

    // ========================================
    // TILBAKESTILL PASSORD - GET
    // ========================================

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult ResetPassword(
        string? userId,
        string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(
                "Tilbakestillingslenken er ugyldig.");
        }

        var model = new ResetPasswordViewModel
        {
            UserId = userId,
            Code = code
        };

        return View(model);
    }

    // ========================================
    // TILBAKESTILL PASSORD - POST
    // ========================================

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        ApplicationUser? user =
            await _userManager.FindByIdAsync(
                model.UserId);

        if (user is null)
        {
            ModelState.AddModelError(
                string.Empty,
                "Lenken er ugyldig eller utløpt. " +
                "Be om en ny tilbakestillingslenke.");

            return View(model);
        }

        string token;

        try
        {
            byte[] tokenBytes =
                WebEncoders.Base64UrlDecode(
                    model.Code);

            token =
                Encoding.UTF8.GetString(tokenBytes);
        }
        catch (FormatException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Tilbakestillingslenken er ugyldig.");

            return View(model);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Tilbakestillingslenken er ugyldig.");

            return View(model);
        }

        IdentityResult result =
            await _userManager.ResetPasswordAsync(
                user,
                token,
                model.NewPassword);

        if (!result.Succeeded)
        {
            foreach (IdentityError error in result.Errors)
            {
                if (error.Code == "InvalidToken")
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Lenken er ugyldig eller utløpt. " +
                        "Be om en ny tilbakestillingslenke.");
                }
                else
                {
                    ModelState.AddModelError(
                        nameof(model.NewPassword),
                        error.Description);
                }
            }

            return View(model);
        }

        return RedirectToAction(
            nameof(ResetPasswordConfirmation));
    }

    // ========================================
    // PASSORD ENDRET
    // ========================================

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public IActionResult ResetPasswordConfirmation()
    {
        return View();
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
        // NYTT: BYGG TELEFONNUMMER
        // ========================================

        // Eksempel:
        //
        // PhoneCountryCode = "+47"
        // PhoneNumber = "123 45 678"
        //
        // Resultat som lagres:
        // "+4712345678"

        string? fullPhoneNumber =
            BuildPhoneNumber(
                model.PhoneCountryCode,
                model.PhoneNumber);

        // ========================================
        // OPPRETT BRUKER
        // ========================================

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            Name = model.Name.Trim(),

            // NYTT:
            // Lagres i Identity sitt PhoneNumber-felt.
            PhoneNumber = fullPhoneNumber,

            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = false
        };

        IdentityResult createResult =
            await _userManager.CreateAsync(
                user,
                model.Password);

        if (!createResult.Succeeded)
        {
            string errors = string.Join(
                " ",
                createResult.Errors.Select(
                    error => error.Description));

            TempData["RegisterError"] = errors;

            return RedirectToAction("Index", "Home");
        }

        // ========================================
        // TILDEL RESOURCEPROVIDER-ROLLE
        // ========================================

        IdentityResult roleResult =
            await _userManager.AddToRoleAsync(
                user,
                IdentitySeeder.ResourceProviderRole);

        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);

            string errors = string.Join(
                " ",
                roleResult.Errors.Select(
                    error => error.Description));

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
                user);

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
                    confirmationToken));

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
            protocol: Request.Scheme);

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
                confirmationUrl);
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
        // KLARGJØR REGISTRERINGSPOPUP
        // ========================================

        HttpContext.Session.Clear();

        HttpContext.Session.SetString(
            PendingRegistrationUserIdKey,
            user.Id);

        return RedirectToAction(
            nameof(RegistrationPending));
    }

    // ========================================
    // REGISTRERING VENTER
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
                PendingRegistrationUserIdKey);

        if (string.IsNullOrWhiteSpace(pendingUserId))
        {
            return RedirectToAction(
                "Index",
                "Home");
        }

        ApplicationUser? user =
            await _userManager.FindByIdAsync(
                pendingUserId);

        if (user is null)
        {
            HttpContext.Session.Remove(
                PendingRegistrationUserIdKey);

            return RedirectToAction(
                "Index",
                "Home");
        }

        bool emailConfirmed =
            await _userManager.IsEmailConfirmedAsync(user);

        bool twoFactorEnabled =
            await _userManager.GetTwoFactorEnabledAsync(user);

        ViewData["RegistrationComplete"] =
            emailConfirmed && twoFactorEnabled;

        return View();
    }

    // ========================================
    // DEMO PUBLIC ACTOR
    // ========================================

    // Beholdes på serveren.
    // Knappen vises ikke på forsiden.

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
                IdentitySeeder.PublicActorEmail);

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
                IdentitySeeder.PublicActorRole);

        if (!hasCorrectRole)
        {
            TempData["LoginError"] =
                "Offentlig testbruker mangler riktig rolle.";

            return RedirectToAction("Index", "Home");
        }

        bool twoFactorEnabled =
            await _userManager.GetTwoFactorEnabledAsync(
                publicActor);

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
            isPersistent: false);

        CreateSession(
            publicActor,
            IdentitySeeder.PublicActorRole);

        await this.AuditAsync(
            AuditActions.Login,
            "Demoinnlogging som offentlig aktør",
            publicActor.Id,
            publicActor.Email);

        return RedirectToAction(
            "Dashboard",
            "Home");
    }

    // ========================================
    // LOGOUT
    // ========================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        // Logges før utlogging, mens brukeren fortsatt er kjent.
        await this.AuditAsync(AuditActions.Logout, "Logget ut");

        await _signInManager.SignOutAsync();

        HttpContext.Session.Clear();

        return RedirectToAction(
            "Index",
            "Home");
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
            "Home");
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
                StringComparison.OrdinalIgnoreCase);

        if (emailChanged)
        {
            ApplicationUser? userWithEmail =
                await _userManager.FindByEmailAsync(
                    newEmail);

            if (userWithEmail is not null &&
                userWithEmail.Id != user.Id)
            {
                ModelState.AddModelError(
                    nameof(ProfileViewModel.Email),
                    "Det finnes allerede en bruker " +
                    "med denne e-postadressen.");

                return View(model);
            }

            ApplicationUser? userWithUserName =
                await _userManager.FindByNameAsync(
                    newEmail);

            if (userWithUserName is not null &&
                userWithUserName.Id != user.Id)
            {
                ModelState.AddModelError(
                    nameof(ProfileViewModel.Email),
                    "Denne e-postadressen kan ikke brukes.");

                return View(model);
            }

            if (_emailSender is null)
            {
                ModelState.AddModelError(
                    nameof(ProfileViewModel.Email),
                    "E-posttjenesten er ikke tilgjengelig. " +
                    "E-postadressen er ikke endret.");

                return View(model);
            }
        }

        // ========================================
        // OPPDATER NAVN OG TELEFON
        // ========================================

        user.Name = model.Name.Trim();

        user.PhoneNumber =
            BuildPhoneNumber(
                model.PhoneCountryCode,
                model.PhoneNumber);

        IdentityResult updateResult =
            await _userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            foreach (IdentityError error in updateResult.Errors)
            {
                ModelState.AddModelError(
                    string.Empty,
                    error.Description);
            }

            return View(model);
        }

        await _signInManager.RefreshSignInAsync(user);

        // ========================================
        // OPPDATER SESSION
        // ========================================

        HttpContext.Session.SetString(
            "UserName",
            user.Name);

        HttpContext.Session.SetString(
            "UserEmail",
            currentEmail);

        HttpContext.Session.SetString(
            "ContactEmail",
            currentEmail);

        HttpContext.Session.SetString(
            "PhoneCountryCode",
            model.PhoneCountryCode);

        if (string.IsNullOrWhiteSpace(model.PhoneNumber))
        {
            HttpContext.Session.Remove(
                "PhoneNumber");
        }
        else
        {
            HttpContext.Session.SetString(
                "PhoneNumber",
                model.PhoneNumber.Trim());
        }

        if (!emailChanged)
        {
            TempData["ProfileSuccess"] =
                "Profilen ble oppdatert.";

            return RedirectToAction(
                nameof(Profile));
        }

        // ========================================
        // GENERER TOKEN FOR NY E-POST
        // ========================================

        string changeToken =
            await _userManager.GenerateChangeEmailTokenAsync(
                user,
                newEmail);

        if (string.IsNullOrWhiteSpace(changeToken))
        {
            ModelState.AddModelError(
                nameof(ProfileViewModel.Email),
                "Navn og telefon er lagret, men " +
                "vi kunne ikke lage en bekreftelseslenke. " +
                "Den gamle e-postadressen er fortsatt aktiv.");

            return View(model);
        }

        string encodedToken =
            WebEncoders.Base64UrlEncode(
                Encoding.UTF8.GetBytes(
                    changeToken));

        string? confirmationUrl = Url.Action(
            action: "ConfirmChange",
            controller: "EmailVerification",
            values: new
            {
                userId = user.Id,
                newEmail = newEmail,
                code = encodedToken
            },
            protocol: Request.Scheme);

        if (string.IsNullOrWhiteSpace(confirmationUrl))
        {
            ModelState.AddModelError(
                nameof(ProfileViewModel.Email),
                "Navn og telefon er lagret, men " +
                "bekreftelseslenken kunne ikke lages. " +
                "Den gamle e-postadressen er fortsatt aktiv.");

            return View(model);
        }

        // ========================================
        // SEND BEKREFTELSE TIL NY E-POST
        // ========================================

        try
        {
            await _emailSender!.SendConfirmationAsync(
                newEmail,
                confirmationUrl);
        }
        catch (Exception)
        {
            ModelState.AddModelError(
                nameof(ProfileViewModel.Email),
                "Navn og telefon er lagret, men " +
                "vi klarte ikke å sende bekreftelseslenken. " +
                "Den gamle e-postadressen fungerer fortsatt. " +
                "Prøv igjen senere.");

            return View(model);
        }

        TempData["ProfileSuccess"] =
            "Profilopplysningene er lagret. " +
            "Vi har sendt en bekreftelseslenke " +
            "til den nye e-postadressen. " +
            "Den gamle adressen fungerer " +
            "frem til den nye er bekreftet.";

        return RedirectToAction(
            nameof(Profile));
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
            if (phoneNumber.StartsWith(
                countryCode,
                StringComparison.Ordinal))
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
            email);

        HttpContext.Session.SetString(
            "UserName",
            name);

        HttpContext.Session.SetString(
            "UserRole",
            role);

        HttpContext.Session.SetString(
            "ContactEmail",
            email);

        var phone =
            SplitPhoneNumber(user.PhoneNumber);

        HttpContext.Session.SetString(
            "PhoneCountryCode",
            phone.CountryCode);

        if (string.IsNullOrWhiteSpace(phone.Number))
        {
            HttpContext.Session.Remove(
                "PhoneNumber");
        }
        else
        {
            HttpContext.Session.SetString(
                "PhoneNumber",
                phone.Number);
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

        // ========================================
        // NYTT: BEHOLD TELEFON VED FEIL
        // ========================================

        TempData["RegisterPhoneCountryCode"] =
            model.PhoneCountryCode?.Trim()
            ?? "+47";

        TempData["RegisterPhoneNumber"] =
            model.PhoneNumber?.Trim()
            ?? string.Empty;
    }
}
