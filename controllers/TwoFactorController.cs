
using System.Globalization;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer e-postbasert tofaktorautentisering etter at
/// brukerens passord er godkjent av ASP.NET Core Identity.
/// </summary>
public class TwoFactorController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailCodeSender _emailSender;
    private readonly ILogger<TwoFactorController> _logger;

    private const long ResendCooldownSeconds = 60;

    // Metadata om kodeutsending lagres i sesjonen.
    // Selve sikkerhetskoden lagres aldri her.
    private const string SentToUserKey =
        "TwoFactorEmailSentToUser";

    private const string LastSentTimeKey =
        "TwoFactorEmailLastSentUtc";

    // Samme nøkkel brukes av AccountController.
    private const string PendingTwoFactorRememberMeKey =
        "PendingTwoFactorRememberMe";

    /// <summary>
    /// Oppretter kontrolleren med Identity, e-posttjenesten
    /// og logging.
    /// </summary>
    /// <param name="userManager">
    /// Administrerer Identity-brukere og tofaktortokener.
    /// </param>
    /// <param name="signInManager">
    /// Håndterer autentisering og tofaktorinnlogging.
    /// </param>
    /// <param name="emailSender">
    /// Sender sikkerhetskoder til brukerens e-postadresse.
    /// </param>
    /// <param name="logger">
    /// Logger feil og relevante sikkerhetshendelser.
    /// </param>
    public TwoFactorController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailCodeSender emailSender,
        ILogger<TwoFactorController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailSender = emailSender;
        _logger = logger;
    }

    #region Tofaktorinnlogging

    /// <summary>
    /// Viser tofaktorskjemaet og sender en sikkerhetskode
    /// ved første besøk i den pågående innloggingsflyten.
    /// </summary>
    /// <returns>
    /// Tofaktorskjemaet eller en omdirigering til forsiden.
    /// </returns>
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> Login()
    {
        // Identity oppretter en midlertidig tofaktorkontekst
        // etter at brukeren har oppgitt riktig passord.
        ApplicationUser? user =
            await _signInManager
                .GetTwoFactorAuthenticationUserAsync();

        if (user is null)
        {
            return RedirectToAction(
                "Index",
                "Home");
        }

        // Ikke send nye koder mens kontoen er låst.
        if (await _userManager.IsLockedOutAsync(user))
        {
            ModelState.AddModelError(
                string.Empty,
                "Kontoen er midlertidig låst " +
                "etter for mange feilaktige forsøk.");

            return View(new TwoFactorLoginViewModel());
        }

        // Forhindre ny utsending ved oppdatering av siden.
        string? previouslySentTo =
            HttpContext.Session.GetString(
                SentToUserKey);

        if (!string.Equals(
            previouslySentTo,
            user.Id,
            StringComparison.Ordinal))
        {
            await SendEmailCodeAsync(user);
        }

        return View(new TwoFactorLoginViewModel());
    }

    /// <summary>
    /// Validerer brukerens tofaktorkode gjennom Identity
    /// og oppretter innloggingssesjonen ved godkjent kode.
    /// </summary>
    /// <param name="model">
    /// Skjemadata som inneholder sikkerhetskoden.
    /// </param>
    /// <returns>
    /// Dashboard ved godkjent innlogging, ellers
    /// tofaktorskjemaet med en feilmelding.
    /// </returns>
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> Login(
        TwoFactorLoginViewModel model)
    {
        ApplicationUser? user =
            await _signInManager
                .GetTwoFactorAuthenticationUserAsync();

        if (user is null)
        {
            return RedirectToAction(
                "Index",
                "Home");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(model.Code))
        {
            ModelState.AddModelError(
                nameof(model.Code),
                "Skriv inn sikkerhetskoden.");

            return View(model);
        }

        // Bare bekreftede e-postadresser kan brukes.
        if (!await _userManager.IsEmailConfirmedAsync(user))
        {
            ModelState.AddModelError(
                string.Empty,
                "E-postadressen er ikke bekreftet.");

            return View(model);
        }

        // AccountController lagrer Husk meg-valget etter
        // godkjent passord. Manglende verdi gir false.
        bool rememberMe =
            string.Equals(
                HttpContext.Session.GetString(
                    PendingTwoFactorRememberMeKey),
                "true",
                StringComparison.Ordinal);

        // Identity validerer koden og håndterer
        // mislykkede forsøk og kontolåsing.
        //
        // rememberClient er false fordi en ny
        // innlogging fortsatt skal kreve tofaktor.
        var result =
            await _signInManager.TwoFactorSignInAsync(
                TokenOptions.DefaultEmailProvider,
                model.Code.Trim(),
                isPersistent: rememberMe,
                rememberClient: false);

        if (result.IsLockedOut)
        {
            await this.AuditAsync(
                AuditActions.LoginFailed,
                "Kontoen ble låst under tofaktorinnlogging",
                user.Id,
                user.Email);

            ModelState.AddModelError(
                string.Empty,
                "Kontoen er midlertidig låst " +
                "etter for mange feilaktige forsøk.");

            return View(model);
        }

        if (!result.Succeeded)
        {
            await this.AuditAsync(
                AuditActions.LoginFailed,
                "Feil eller utløpt tofaktorkode",
                user.Id,
                user.Email);

            ModelState.AddModelError(
                nameof(model.Code),
                "Ugyldig eller utløpt sikkerhetskode. " +
                "Prøv igjen.");

            return View(model);
        }

        // Identity har nå opprettet autentiseringscookien.
        // Portalens eksisterende sesjonsdata opprettes
        // først etter vellykket tofaktorinnlogging.
        await CreateLegacySessionAsync(user);

        await this.AuditAsync(
            AuditActions.Login,
            "Innlogging med tofaktorkode",
            user.Id,
            user.Email);

        return RedirectToAction(
            "Dashboard",
            "Home");
    }

    #endregion

    #region Utsending av sikkerhetskoder

    /// <summary>
    /// Sender en ny tofaktorkode når ventetiden mellom
    /// utsendingene er utløpt.
    /// </summary>
    /// <returns>
    /// Omdirigering til tofaktorskjemaet eller skjemaet
    /// med en feilmelding.
    /// </returns>
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> ResendCode()
    {
        ApplicationUser? user =
            await _signInManager
                .GetTwoFactorAuthenticationUserAsync();

        if (user is null)
        {
            return RedirectToAction(
                "Index",
                "Home");
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            ModelState.AddModelError(
                string.Empty,
                "Kontoen er midlertidig låst. " +
                "Du kan ikke be om en ny kode ennå.");

            return View(
                "Login",
                new TwoFactorLoginViewModel());
        }

        // Begrens gjentatte e-postutsendinger innenfor
        // den samme innloggingssesjonen.
        if (IsWithinCooldown(user))
        {
            ModelState.AddModelError(
                string.Empty,
                "Du må vente minst 60 sekunder " +
                "før du kan be om en ny kode.");

            return View(
                "Login",
                new TwoFactorLoginViewModel());
        }

        bool sent = await SendEmailCodeAsync(user);

        if (!sent)
        {
            return View(
                "Login",
                new TwoFactorLoginViewModel());
        }

        // TempData brukes fordi meldingen skal overleve
        // omdirigeringen tilbake til tofaktorskjemaet.
        TempData["TwoFactorMessage"] =
            "En sikkerhetskode er sendt til e-posten din.";

        return RedirectToAction(nameof(Login));
    }

    /// <summary>
    /// Genererer en tofaktorkode gjennom Identity og
    /// sender den til brukerens bekreftede e-postadresse.
    /// </summary>
    /// <param name="user">
    /// Brukeren som venter på tofaktorautentisering.
    /// </param>
    /// <returns>
    /// True dersom e-posttjenesten godtok utsendingen,
    /// ellers false.
    /// </returns>
    private async Task<bool> SendEmailCodeAsync(
        ApplicationUser user)
    {
        string? email =
            await _userManager.GetEmailAsync(user);

        if (string.IsNullOrWhiteSpace(email) ||
            !await _userManager.IsEmailConfirmedAsync(user))
        {
            ModelState.AddModelError(
                string.Empty,
                "Kontoen har ikke en bekreftet " +
                "e-postadresse.");

            return false;
        }

        // Kontroller også kontolåsing her, slik at
        // hjelpemetoden ikke kan sende koder til låste kontoer.
        if (await _userManager.IsLockedOutAsync(user))
        {
            ModelState.AddModelError(
                string.Empty,
                "Kontoen er midlertidig låst. " +
                "Sikkerhetskoden kunne ikke sendes.");

            return false;
        }

        // Bruk den registrerte Identity-tokenleverandøren.
        // Koden lagres ikke i sesjonen eller applikasjonsloggen.
        string code =
            await _userManager.GenerateTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultEmailProvider);

        if (string.IsNullOrWhiteSpace(code))
        {
            _logger.LogWarning(
                "Kunne ikke generere tofaktorkode for bruker {UserId}.",
                user.Id);

            ModelState.AddModelError(
                string.Empty,
                "Kunne ikke generere sikkerhetskode.");

            return false;
        }

        try
        {
            await _emailSender.SendCodeAsync(
                email,
                code);
        }
        catch (Exception exception)
        {
            // Logg feilen, men aldri sikkerhetskoden.
            _logger.LogError(
                exception,
                "Kunne ikke sende tofaktorkode for bruker {UserId}.",
                user.Id);

            ModelState.AddModelError(
                string.Empty,
                "Kunne ikke sende sikkerhetskoden. " +
                "Prøv igjen senere.");

            return false;
        }

        // Registrer utsending først etter at e-posttjenesten
        // har godtatt meldingen.
        HttpContext.Session.SetString(
            SentToUserKey,
            user.Id);

        HttpContext.Session.SetString(
            LastSentTimeKey,
            DateTimeOffset.UtcNow
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture));

        return true;
    }

    /// <summary>
    /// Kontrollerer om en kode ble sendt til samme bruker
    /// i løpet av de siste 60 sekundene.
    /// </summary>
    /// <param name="user">
    /// Brukeren som ber om en ny sikkerhetskode.
    /// </param>
    /// <returns>
    /// True dersom ventetiden ikke er utløpt,
    /// ellers false.
    /// </returns>
    private bool IsWithinCooldown(
        ApplicationUser user)
    {
        string? sentToUser =
            HttpContext.Session.GetString(
                SentToUserKey);

        if (!string.Equals(
            sentToUser,
            user.Id,
            StringComparison.Ordinal))
        {
            return false;
        }

        string? storedTime =
            HttpContext.Session.GetString(
                LastSentTimeKey);

        if (!long.TryParse(
            storedTime,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out long lastSentUnixTime))
        {
            return false;
        }

        long now =
            DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        long secondsSinceLastSend =
            now - lastSentUnixTime;

        return secondsSinceLastSend >= 0
            && secondsSinceLastSend < ResendCooldownSeconds;
    }

    #endregion

    #region Sesjonsdata

    /// <summary>
    /// Oppretter sesjonsverdiene som eksisterende dashboard-
    /// og profilfunksjoner bruker etter vellykket innlogging.
    /// </summary>
    /// <param name="user">
    /// Den ferdig autentiserte Identity-brukeren.
    /// </param>
    /// <returns>
    /// En oppgave som fullføres når sesjonen er oppdatert.
    /// </returns>
    private async Task CreateLegacySessionAsync(
        ApplicationUser user)
    {
        IList<string> roles =
            await _userManager.GetRolesAsync(user);

        string role =
            roles.Contains(IdentitySeeder.AdminRole)
                ? IdentitySeeder.AdminRole
                : roles.Contains(IdentitySeeder.PublicActorRole)
                    ? IdentitySeeder.PublicActorRole
                    : IdentitySeeder.ResourceProviderRole;

        string email =
            user.Email
            ?? user.UserName
            ?? string.Empty;

        string name =
            string.IsNullOrWhiteSpace(user.Name)
                ? email
                : user.Name;

        // Fjern midlertidige tofaktoropplysninger og
        // Husk meg-valget før ordinære sesjonsdata opprettes.
        HttpContext.Session.Clear();

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

        string phone =
            user.PhoneNumber
            ?? string.Empty;

        string countryCode = "+47";

        foreach (string code in new[]
        {
            "+47",
            "+45",
            "+46"
        })
        {
            if (phone.StartsWith(
                code,
                StringComparison.Ordinal))
            {
                countryCode = code;
                phone = phone[code.Length..];

                break;
            }
        }

        HttpContext.Session.SetString(
            "PhoneCountryCode",
            countryCode);

        if (string.IsNullOrWhiteSpace(phone))
        {
            HttpContext.Session.Remove(
                "PhoneNumber");
        }
        else
        {
            HttpContext.Session.SetString(
                "PhoneNumber",
                phone);
        }
    }

    #endregion
}
