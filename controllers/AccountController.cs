/*
    AccountController.cs – konto og innlogging

    Hva filen gjør: Innlogging, registrering av ny bruker, glemt passord, profil, utlogging og
    sletting av konto.
    Identity gjør det tekniske (sjekker passord, lagrer brukeren, lager koder). Denne
    controlleren styrer rekkefølgen og hva brukeren ser.

    På nettsiden: Innloggings- og registreringsskjemaet på forsiden, «Glemt passord», «Profil»,
    «Logg ut» og «Slett konto».

    Oppgavekrav: 8, 9, 14, 18, 20
*/

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
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;

namespace FirstWebAppInDocker.Controllers;

/*
    Håndterer registrering, innlogging, passord, profil og kontosletting.
    Autentisering og brukeradministrasjon utføres via ASP.NET Core Identity.
*/
public class AccountController : Controller
{
    #region Felt og konstruktør

    private const string PendingRegistrationUserIdKey = "PendingRegistrationUserId";

    private const string PendingTwoFactorRememberMeKey = "PendingTwoFactorRememberMe";

    private readonly UserManager<ApplicationUser> _userManager;

    private readonly SignInManager<ApplicationUser> _signInManager;

    private readonly IWebHostEnvironment? _environment;

    private readonly IEmailCodeSender? _emailSender;

    private readonly ILogger<AccountController>? _logger;

    private readonly ApplicationDbContext? _context;

    // Oppretter kontrolleren med Identity og portalens tjenester.
    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        /*
            Noen avhengigheter er valgfrie, slik at testene kan lage controlleren uten e-post og
            logging.
        */
        IWebHostEnvironment? environment = null,
        IEmailCodeSender? emailSender = null,
        ILogger<AccountController>? logger = null,
        ApplicationDbContext? context = null)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _environment = environment;
        _emailSender = emailSender;
        _logger = logger;
        _context = context;
    }

    #endregion

    #region Innlogging

    /*
        POST /Account/Login – innloggingsskjemaet på forsiden.
    */
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["LoginError"] = "Fyll inn gyldig e-post og passord.";

            return RedirectToAction("Index", "Home");
        }

        string email = model.Email.Trim();

        ApplicationUser? user = await _userManager.FindByEmailAsync(email);

        /*
            Samme feilmelding uansett om e-posten ikke finnes, passordet er feil eller kontoen
            ikke er bekreftet. Da kan ingen bruke skjemaet til å finne ut hvilke e-poster som er
            registrert.
        */
        if (user is null)
        {
            await this.AuditAsync(
                AuditActions.LoginFailed,
                "Ukjent e-postadresse",
                userEmailOverride: email);

            TempData["LoginError"] = "Feil e-post eller passord, eller kontoen er ikke bekreftet.";

            return RedirectToAction("Index", "Home");
        }

        if (!user.EmailConfirmed)
        {
            // Samme feilmelding som ved ukjent e-post eller feil passord.
            TempData["LoginError"] = "Feil e-post eller passord, eller kontoen er ikke bekreftet.";

            return RedirectToAction("Index", "Home");
        }

        HttpContext.Session.Remove(PendingTwoFactorRememberMeKey);

        Microsoft.AspNetCore.Identity.SignInResult result =
            /*
                Identity sjekker passordet og teller feil forsøk (låser etter 5). isPersistent
                er «Husk meg».
            */
            await _signInManager.PasswordSignInAsync(
                user,
                model.Password,
                isPersistent: model.RememberMe,
                lockoutOnFailure: true);

        if (result.Succeeded)
        {
            string role = await GetPrimaryRoleAsync(user);

            CreateSession(user, role);

            await this.AuditAsync(
                AuditActions.Login,
                $"Innlogging med passord ({role})",
                user.Id,
                user.Email);

            return RedirectToAction("Dashboard", "Home");
        }

        /*
            Passordet var riktig, men brukeren har tofaktor på.
            På nettsiden: man sendes videre til siden der man skriver inn koden fra e-posten.
        */
        if (result.RequiresTwoFactor)
        {
            HttpContext.Session.SetString(
                PendingTwoFactorRememberMeKey,
                model.RememberMe ? "true" : "false");

            return RedirectToAction("Login", "TwoFactor");
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

            return RedirectToAction("Index", "Home");
        }

        await this.AuditAsync(AuditActions.LoginFailed, "Feil passord", user.Id, user.Email);

        TempData["LoginError"] = "Feil e-post eller passord, eller kontoen er ikke bekreftet.";

        return RedirectToAction("Index", "Home");
    }

    #endregion

    #region Glemt passord

    /*
        GET /Account/ForgotPassword
        Viser skjema for å be om passordtilbakestilling.

        Returnerer: Et MVC-resultat.
    */
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }

    /*
        POST /Account/ForgotPassword
        «Glemt passord». Lager en engangslenke og sender den på e-post. Man får samme
        melding uansett om e-posten finnes eller ikke.
    */
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        string email = model.Email.Trim();

        ApplicationUser? user = await _userManager.FindByEmailAsync(email);

        // Samme svar uansett om e-postadressen finnes.

        if (user is null)
        {
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        if (!await _userManager.IsEmailConfirmedAsync(user))
        {
            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        if (_emailSender is null)
        {
            _logger?.LogWarning(
                "E-posttjenesten er ikke tilgjengelig " +
                "for tilbakestilling av passord.");

            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        string token = await _userManager.GeneratePasswordResetTokenAsync(user);

        // Gjør koden om til tegn som kan stå trygt i en lenke.
        string encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

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
            _logger?.LogWarning("Kunne ikke generere lenke for " + "tilbakestilling av passord.");

            return RedirectToAction(nameof(ForgotPasswordConfirmation));
        }

        try
        {
            await _emailSender.SendPasswordResetAsync(email, resetUrl);
        }
        catch (Exception exception)
        {
            _logger?.LogError(
                exception,
                "Kunne ikke sende passordtilbakestilling " +
                "for bruker {UserId}.",
                user.Id);
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    /*
        GET /Account/ForgotPasswordConfirmation
        Viser en generell bekreftelse etter forespørsel om passordtilbakestilling.

        Returnerer: Et MVC-resultat.
    */
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }

    /*
        GET /Account/ResetPassword
        Viser skjema for tilbakestilling dersom lenkens parametere finnes.

        userId: Brukeridentifikator fra lenken.
        code: Kodet tilbakestillingstoken.
        Returnerer: Et MVC-resultat.
    */
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult ResetPassword(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return BadRequest("Tilbakestillingslenken er ugyldig.");
        }

        ResetPasswordViewModel model = new ResetPasswordViewModel
        {
            UserId = userId,
            Code = code
        };

        return View(model);
    }

    /*
        POST /Account/ResetPassword
        Siden man kommer til fra lenken i e-posten. Koden må være gyldig og ikke utløpt.
    */
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(model.UserId);

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
            byte[] tokenBytes = WebEncoders.Base64UrlDecode(model.Code);

            token = Encoding.UTF8.GetString(tokenBytes);
        }
        catch (FormatException)
        {
            ModelState.AddModelError(string.Empty, "Tilbakestillingslenken er ugyldig.");

            return View(model);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(string.Empty, "Tilbakestillingslenken er ugyldig.");

            return View(model);
        }

        IdentityResult result =
            await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

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
                    ModelState.AddModelError(nameof(model.NewPassword), error.Description);
                }
            }

            return View(model);
        }

        return RedirectToAction(nameof(ResetPasswordConfirmation));
    }

    /*
        GET /Account/ResetPasswordConfirmation
        Viser bekreftelse etter vellykket passordtilbakestilling.

        Returnerer: Et MVC-resultat.
    */
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }

    #endregion

    #region Registrering

    /*
        POST /Account/Register – registreringsskjemaet på forsiden.
        Nye brukere får alltid rollen frivillig (ResourceProvider). Offentlige aktører lages
        bare av admin.
    */
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
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

        // Opprett ikke en konto dersom bekreftelsesmail ikke kan sendes.
        if (_emailSender is null)
        {
            _logger?.LogError("Registrering avvist fordi e-posttjenesten ikke er konfigurert.");

            TempData["RegisterError"] =
                "Registrering er midlertidig utilgjengelig. " +
                "Kontakt administrator.";

            return RedirectToAction("Index", "Home");
        }

        string email = model.Email.Trim();

        ApplicationUser? existingUser = await _userManager.FindByEmailAsync(email);

        if (existingUser is not null)
        {
            TempData["RegisterError"] =
                "Det finnes allerede en bruker " +
                "med denne e-postadressen.";

            return RedirectToAction("Index", "Home");
        }

        string? fullPhoneNumber = BuildPhoneNumber(model.PhoneCountryCode, model.PhoneNumber);

        ApplicationUser user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            Name = model.Name.Trim(),
            PhoneNumber = fullPhoneNumber,
            CreatedAt = DateTime.UtcNow,
            // Kontoen virker ikke før e-posten er bekreftet med lenken man får tilsendt.
            EmailConfirmed = false
        };

        /*
            Lagrer den nye brukeren i databasen (tabellen AspNetUsers). Passordet lagres aldri
            som tekst, bare som en hash.
        */
        IdentityResult createResult = await _userManager.CreateAsync(user, model.Password);

        if (!createResult.Succeeded)
        {
            string errors = string.Join(
                " ",
                createResult.Errors.Select(error => error.Description));

            TempData["RegisterError"] = errors;

            return RedirectToAction("Index", "Home");
        }

        IdentityResult roleResult =
            await _userManager.AddToRoleAsync(user, IdentitySeeder.ResourceProviderRole);

        if (!roleResult.Succeeded)
        {
            /*
                Kunne ikke rollen settes, slettes brukeren igjen, så vi ikke står igjen med en
                halvferdig konto.
            */
            await _userManager.DeleteAsync(user);

            string errors = string.Join(" ", roleResult.Errors.Select(error => error.Description));

            TempData["RegisterError"] =
                "Brukeren kunne ikke få riktig rolle. " +
                errors;

            return RedirectToAction("Index", "Home");
        }

        string confirmationToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        if (string.IsNullOrWhiteSpace(confirmationToken))
        {
            TempData["RegisterError"] =
                "Kontoen ble opprettet, men " +
                "bekreftelseslenken kunne ikke lages.";

            return RedirectToAction("Index", "Home");
        }

        string encodedToken =
            WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(confirmationToken));

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

        try
        {
            await _emailSender.SendConfirmationAsync(email, confirmationUrl);
        }
        catch (Exception exception)
        {
            _logger?.LogError(
                exception,
                "Bekreftelsesmail kunne ikke sendes til bruker {UserId}.",
                user.Id);

            TempData["RegisterError"] =
                "Kontoen ble opprettet, men vi " +
                "klarte ikke å sende bekreftelsesmail. " +
                "Kontakt administrator.";

            return RedirectToAction("Index", "Home");
        }

        HttpContext.Session.Clear();

        HttpContext.Session.SetString(PendingRegistrationUserIdKey, user.Id);

        return RedirectToAction(nameof(RegistrationPending));
    }

    /*
        GET /Account/RegistrationPending
        Siden «Sjekk e-posten din». Den spør serveren med jevne mellomrom om e-posten er
        bekreftet, og går videre av seg selv.
    */
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> RegistrationPending()
    {
        string? pendingUserId = HttpContext.Session.GetString(PendingRegistrationUserIdKey);

        if (string.IsNullOrWhiteSpace(pendingUserId))
        {
            return RedirectToAction("Index", "Home");
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(pendingUserId);

        if (user is null)
        {
            HttpContext.Session.Remove(PendingRegistrationUserIdKey);

            return RedirectToAction("Index", "Home");
        }

        bool emailConfirmed = await _userManager.IsEmailConfirmedAsync(user);

        bool twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);

        ViewData["RegistrationComplete"] = emailConfirmed && twoFactorEnabled;

        return View();
    }

    #endregion

    #region Demoinnlogging, utlogging og ingen tilgang

    /*
        POST /Account/DemoPublicActorLogin
        Demo-knapp for å logge inn som offentlig aktør. Virker bare i utviklingsmiljøet.
    */
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoPublicActorLogin()
    {
        if (_environment?.IsDevelopment() != true)
        {
            return NotFound();
        }

        ApplicationUser? publicActor =
            await _userManager.FindByEmailAsync(IdentitySeeder.PublicActorEmail);

        if (publicActor is null)
        {
            TempData["LoginError"] =
                "Offentlig testbruker finnes " +
                "ikke i databasen.";

            return RedirectToAction("Index", "Home");
        }

        bool hasCorrectRole =
            await _userManager.IsInRoleAsync(publicActor, IdentitySeeder.PublicActorRole);

        if (!hasCorrectRole)
        {
            TempData["LoginError"] = "Offentlig testbruker mangler riktig rolle.";

            return RedirectToAction("Index", "Home");
        }

        bool twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(publicActor);

        if (twoFactorEnabled)
        {
            TempData["LoginError"] =
                "Demoinnlogging kan ikke brukes " +
                "når tofaktorautentisering er aktivert. " +
                "Logg inn med e-post og passord.";

            return RedirectToAction("Index", "Home");
        }

        await _signInManager.SignOutAsync();

        await _signInManager.SignInAsync(publicActor, isPersistent: false);

        CreateSession(publicActor, IdentitySeeder.PublicActorRole);

        await this.AuditAsync(
            AuditActions.Login,
            "Demoinnlogging som offentlig aktør",
            publicActor.Id,
            publicActor.Email);

        return RedirectToAction("Dashboard", "Home");
    }

    /*
        POST /Account/Logout
        «Logg ut». Utlogging er et skjema (POST), ikke en vanlig lenke, så en annen
        nettside ikke kan logge deg ut.
    */
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await this.AuditAsync(AuditActions.Logout, "Logget ut");

        await _signInManager.SignOutAsync();

        HttpContext.Session.Clear();

        return RedirectToAction("Index", "Home");
    }

    /*
        GET /Account/AccessDenied
        Viser en tilgangsfeil via forsiden.

        Returnerer: Et MVC-resultat.
    */
    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        TempData["LoginError"] =
            "Du har ikke tilgang til denne " +
            "delen av Beredskapsportalen.";

        return RedirectToAction("Index", "Home");
    }

    #endregion

    #region Profil

    /*
        GET /Account/Profile
        «Profil»: navn, telefon og e-post.
    */
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        ApplicationUser? user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        (string CountryCode, string? Number) phone = SplitPhoneNumber(user.PhoneNumber);

        ProfileViewModel model = new ProfileViewModel
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

    /*
        POST /Account/Profile
        Oppdaterer profilopplysninger og starter bekreftelse ved e-postendring.

        model: Endrede profilopplysninger.
        Returnerer: Et MVC-resultat.
    */
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        ApplicationUser? user = await _userManager.GetUserAsync(User);

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
            !string.Equals(currentEmail, newEmail, StringComparison.OrdinalIgnoreCase);

        if (emailChanged)
        {
            ApplicationUser? userWithEmail = await _userManager.FindByEmailAsync(newEmail);

            if (userWithEmail is not null && userWithEmail.Id != user.Id)
            {
                ModelState.AddModelError(
                    nameof(ProfileViewModel.Email),
                    "Det finnes allerede en bruker " +
                    "med denne e-postadressen.");

                return View(model);
            }

            ApplicationUser? userWithUserName = await _userManager.FindByNameAsync(newEmail);

            if (userWithUserName is not null && userWithUserName.Id != user.Id)
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

        user.Name = model.Name.Trim();

        user.PhoneNumber = BuildPhoneNumber(model.PhoneCountryCode, model.PhoneNumber);

        IdentityResult updateResult = await _userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            foreach (IdentityError error in updateResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        await _signInManager.RefreshSignInAsync(user);

        HttpContext.Session.SetString("UserName", user.Name);

        HttpContext.Session.SetString("UserEmail", currentEmail);

        HttpContext.Session.SetString("ContactEmail", currentEmail);

        HttpContext.Session.SetString("PhoneCountryCode", model.PhoneCountryCode);

        if (string.IsNullOrWhiteSpace(model.PhoneNumber))
        {
            HttpContext.Session.Remove("PhoneNumber");
        }
        else
        {
            HttpContext.Session.SetString("PhoneNumber", model.PhoneNumber.Trim());
        }

        if (!emailChanged)
        {
            TempData["ProfileSuccess"] = "Profilen ble oppdatert.";

            return RedirectToAction(nameof(Profile));
        }

        // Ny e-post må bekreftes fra den nye adressen før den tas i bruk.
        string changeToken = await _userManager.GenerateChangeEmailTokenAsync(user, newEmail);

        if (string.IsNullOrWhiteSpace(changeToken))
        {
            ModelState.AddModelError(
                nameof(ProfileViewModel.Email),
                "Navn og telefon er lagret, men " +
                "vi kunne ikke lage en bekreftelseslenke. " +
                "Den gamle e-postadressen er fortsatt aktiv.");

            return View(model);
        }

        string encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(changeToken));

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

        try
        {
            await _emailSender!.SendConfirmationAsync(newEmail, confirmationUrl);
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

        return RedirectToAction(nameof(Profile));
    }

    #endregion

    #region Slett konto

    /*
        POST /Account/DeleteAccount
        «Slett konto». Krever avkrysning og riktig passord. Admin kan ikke slette seg
        selv her.

        Sletter brukerens konto og tilhørende data i én databasetransaksjon.

        currentPassword: Brukerens gjeldende passord.
        confirmDeletion: Bekreftelse på permanent sletting.
        Returnerer: Et MVC-resultat.
    */
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAccount(string? currentPassword, bool confirmDeletion)
    {
        ApplicationUser? user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Challenge();
        }

        // Krev bekreftelse fra profilsiden.
        if (!confirmDeletion)
        {
            TempData["ProfileError"] = "Du må bekrefte at du vil slette kontoen.";

            return RedirectToAction(nameof(Profile));
        }

        // Krev riktig passord.
        if (string.IsNullOrWhiteSpace(currentPassword) ||
            !await _userManager.CheckPasswordAsync(user, currentPassword))
        {
            TempData["ProfileError"] = "Passordet er feil. Kontoen er ikke slettet.";

            return RedirectToAction(nameof(Profile));
        }

        // Administratorer må fortsatt beskyttes.
        if (await _userManager.IsInRoleAsync(user, IdentitySeeder.AdminRole))
        {
            TempData["ProfileError"] =
                "Administratorkontoer kan ikke slettes " +
                "fra profilsiden.";

            return RedirectToAction(nameof(Profile));
        }

        if (_context is null)
        {
            _logger?.LogError("Databasekontekst mangler ved kontosletting.");

            TempData["ProfileError"] =
                "Databasen er ikke tilgjengelig. " +
                "Kontoen er ikke slettet.";

            return RedirectToAction(nameof(Profile));
        }

        try
        {
            /*
                Identity og resten av applikasjonen
                bruker ApplicationDbContext.

                For atomisk sletting må UserManager
                bruke samme scoped DbContext-instans.
            */

            await using IDbContextTransaction transaction =
                /*
                    Alt slettes i én operasjon: ressurser, behov, varsler og brukeren. Går noe
                    galt, angres alt.
                */
                await _context.Database.BeginTransactionAsync();

            string userId = user.Id;
            string? email = user.Email;

            List<Resource> ownedResources =
                await _context.Resources
                    .Where(resource => resource.OwnerUserId == userId)
                    .ToListAsync();

            /*
                Behov fra eldre versjoner kan være
                knyttet til e-post i stedet for bruker-ID.
            */

            List<Need> ownedNeeds =
                await _context.Needs
                    .Where(need =>
                        need.OwnerUserId == userId ||
                        (need.OwnerUserId == null && email != null && need.OwnerEmail == email))
                    .ToListAsync();

            List<Notification> notifications =
                await _context.Notifications
                    .Where(notification => notification.UserId == userId)
                    .ToListAsync();

            /*
                NeedAssignment har Cascade for både
                ResourceId og NeedId i ApplicationDbContext.

                Dermed slettes tilknyttede forespørsler
                og tildelinger når disse oppføringene
                slettes, forutsatt at databaseskjemaet
                har de samme fremmednøkkelreglene.
            */

            _context.Notifications
                .RemoveRange(notifications);

            _context.Resources
                .RemoveRange(ownedResources);

            _context.Needs
                .RemoveRange(ownedNeeds);

            await _context.SaveChangesAsync();

            IdentityResult deleteResult = await _userManager.DeleteAsync(user);

            if (!deleteResult.Succeeded)
            {
                await transaction.RollbackAsync();

                _logger?.LogWarning(
                    "Kontosletting feilet for bruker {UserId}: {Errors}",
                    userId,
                    string.Join(", ", deleteResult.Errors.Select(error => error.Code)));

                TempData["ProfileError"] =
                    "Kontoen kunne ikke slettes. " +
                    "Alle databaseendringene ble rullet tilbake.";

                return RedirectToAction(nameof(Profile));
            }

            // Først her blir slettingen permanent.
            await transaction.CommitAsync();

            _logger?.LogInformation(
                "Bruker {UserId} slettet sin konto, " +
                "{ResourceCount} ressurser, " +
                "{NeedCount} behov og " +
                "{NotificationCount} varsler.",
                userId,
                ownedResources.Count,
                ownedNeeds.Count,
                notifications.Count);
        }
        catch (DbUpdateException exception)
        {
            _logger?.LogError(exception, "Databasefeil under kontosletting for {UserId}.", user.Id);

            TempData["ProfileError"] =
                "Databasen kunne ikke fullføre slettingen. " +
                "Kontroller databaserelasjonene og prøv igjen.";

            return RedirectToAction(nameof(Profile));
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Uventet feil under kontosletting for {UserId}.", user.Id);

            TempData["ProfileError"] =
                "Det oppstod en feil under kontoslettingen. " +
                "Kontroller kontostatus før du prøver igjen.";

            return RedirectToAction(nameof(Profile));
        }

        // Utføres først etter vellykket commit.

        await _signInManager.SignOutAsync();

        HttpContext.Session.Clear();

        TempData["AccountDeleted"] =
            "Kontoen din og tilhørende ressurser " +
            "og behov er slettet permanent.";

        return RedirectToAction("Index", "Home");
    }

    #endregion

    #region Hjelpemetoder

    /*
        Kombinerer landskode og telefonnummer uten mellomrom eller bindestreker.

        countryCode: Telefonens landskode.
        phoneNumber: Lokalt telefonnummer.
        Returnerer: Normalisert telefonnummer eller null.
    */
    private static string? BuildPhoneNumber(string countryCode, string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        string normalizedCountryCode = countryCode.Trim();

        string normalizedPhoneNumber =
            phoneNumber
                .Replace(" ", string.Empty)
                .Replace("-", string.Empty);

        return normalizedCountryCode
            + normalizedPhoneNumber;
    }

    /*
        Deler et lagret telefonnummer i landskode og lokalt nummer.

        phoneNumber: Telefonnummer med eventuell landskode.
        Returnerer: Landskode og lokalt nummer.
    */
    private static (string CountryCode, string? Number) SplitPhoneNumber(string? phoneNumber)
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
            if (phoneNumber.StartsWith(countryCode, StringComparison.Ordinal))
            {
                return (countryCode, phoneNumber[countryCode.Length..]);
            }
        }

        return ("+47", phoneNumber);
    }

    /*
        Velger brukerens primære rolle etter portalens rolleprioritet.

        user: Brukeren som rollene skal hentes for.
        Returnerer: Rollen som brukes til sesjonsinformasjon.
    */
    private async Task<string> GetPrimaryRoleAsync(ApplicationUser user)
    {
        IList<string> roles = await _userManager.GetRolesAsync(user);

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

    /*
        Lagrer navn, rolle og e-post i Session, slik at menyen øverst kan vise dem uten å spørre
        databasen hver gang. Det er fortsatt Identity-cookien som bestemmer om man er innlogget.
    */
    private void CreateSession(ApplicationUser user, string role)
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

        HttpContext.Session.SetString("UserEmail", email);

        HttpContext.Session.SetString("UserName", name);

        HttpContext.Session.SetString("UserRole", role);

        HttpContext.Session.SetString("ContactEmail", email);

        (string CountryCode, string? Number) phone = SplitPhoneNumber(user.PhoneNumber);

        HttpContext.Session.SetString("PhoneCountryCode", phone.CountryCode);

        if (string.IsNullOrWhiteSpace(phone.Number))
        {
            HttpContext.Session.Remove("PhoneNumber");
        }
        else
        {
            HttpContext.Session.SetString("PhoneNumber", phone.Number);
        }
    }

    /*
        Bevarer skjemaverdier ved redirect til eksisterende registreringspopup.

        model: Registreringsdata.
    */
    private void SaveRegistrationValues(RegisterViewModel model)
    {
        TempData["RegisterName"] =
            model.Name?.Trim()
            ?? string.Empty;

        TempData["RegisterEmail"] =
            model.Email?.Trim()
            ?? string.Empty;

        TempData["RegisterPhoneCountryCode"] =
            model.PhoneCountryCode?.Trim()
            ?? "+47";

        TempData["RegisterPhoneNumber"] =
            model.PhoneNumber?.Trim()
            ?? string.Empty;
    }

    #endregion
}
