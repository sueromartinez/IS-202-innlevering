
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

public class TwoFactorController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailCodeSender _emailSender;
    private readonly ILogger<TwoFactorController> _logger;

    // Minimumstid mellom utsendinger.
    private const long ResendCooldownSeconds = 60;

    // Informasjon om e-postutsending lagres
    // i brukerens midlertidige Session.
    private const string SentToUserKey =
        "TwoFactorEmailSentToUser";

    private const string LastSentTimeKey =
        "TwoFactorEmailLastSentUtc";

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

    // ========================================
    // E-POSTINNLOGGING - GET
    // ========================================

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> Login()
    {
        // Identity har allerede kontrollert
        // e-post og passord.
        //
        // Hent brukeren som venter på tofaktor.

        ApplicationUser? user =
            await _signInManager
                .GetTwoFactorAuthenticationUserAsync();

        if (user is null)
        {
            return RedirectToAction(
                "Index",
                "Home");
        }

        // Send kode automatisk første gang
        // brukeren åpner tofaktorsiden.
        //
        // Ved oppdatering av siden skal vi
        // ikke sende en ny e-post hver gang.

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

    // ========================================
    // E-POSTINNLOGGING - POST
    // ========================================

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

        // E-post må være bekreftet før
        // den kan brukes til innlogging.

        if (!await _userManager.IsEmailConfirmedAsync(user))
        {
            ModelState.AddModelError(
                string.Empty,
                "E-postadressen er ikke bekreftet.");

            return View(model);
        }

        // ========================================
        // VERIFISER E-POSTKODEN
        // ========================================
        //
        // Identity kontrollerer koden mot
        // den innebygde Email-tokenleverandøren.
        //
        // Dette er IKKE autentiseringsapp.
        //
        // rememberClient: false betyr at
        // enheten ikke huskes for senere
        // innlogginger.

        var result =
            await _signInManager.TwoFactorSignInAsync(
                TokenOptions.DefaultEmailProvider,
                model.Code.Trim(),
                isPersistent: false,
                rememberClient: false
            );

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(
                string.Empty,
                "Kontoen er midlertidig låst " +
                "etter for mange feilaktige forsøk.");

            return View(model);
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(
                nameof(model.Code),
                "Ugyldig eller utløpt sikkerhetskode. " +
                "Prøv igjen.");

            return View(model);
        }

        // ========================================
        // VELLYKKET TOFAKTORINNLOGGING
        // ========================================
        //
        // Først nå oppretter vi den vanlige
        // Session som resten av prosjektet
        // bruker.

        await CreateLegacySessionAsync(user);

        return RedirectToAction(
            "Dashboard",
            "Home");
    }

    // ========================================
    // SEND NY KODE - POST
    // ========================================

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

        // Unngå at samme innloggingsforsøk
        // sender mange e-poster på kort tid.

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

        bool sent =
            await SendEmailCodeAsync(user);

        if (!sent)
        {
            return View(
                "Login",
                new TwoFactorLoginViewModel());
        }

        TempData["TwoFactorMessage"] =
            "En sikkerhetskode er sendt til e-posten din.";

        return RedirectToAction(nameof(Login));
    }

    // ========================================
    // SEND E-POSTKODE
    // ========================================

    private async Task<bool> SendEmailCodeAsync(
        ApplicationUser user)
    {
        // Kontroller at brukeren har en
        // bekreftet e-postadresse.

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

        // ========================================
        // GENERER SEKSSIFRET KODE
        // ========================================
        //
        // Identity genererer koden.
        //
        // Den er knyttet til brukeren
        // og er gyldig i et begrenset
        // tidsvindu.
        //
        // Vi lagrer ikke koden i Session.

        string code =
            await _userManager.GenerateTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultEmailProvider);

        if (string.IsNullOrWhiteSpace(code))
        {
            ModelState.AddModelError(
                string.Empty,
                "Kunne ikke generere sikkerhetskode.");

            return false;
        }

        // ========================================
        // SEND KODEN
        // ========================================

        try
        {
            await _emailSender.SendCodeAsync(
                email,
                code);
        }
        catch (Exception exception)
        {
            // Logg feilen, men aldri selve
            // sikkerhetskoden.

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

        // Registrer utsendingen først etter
        // at SMTP har godtatt e-posten.

        HttpContext.Session.SetString(
            SentToUserKey,
            user.Id);

        HttpContext.Session.SetString(
            LastSentTimeKey,
            DateTimeOffset.UtcNow
                .ToUnixTimeSeconds()
                .ToString(
                    System.Globalization.CultureInfo.InvariantCulture));

        return true;
    }

    // ========================================
    // KONTROLLER VENTETID MELLOM E-POSTER
    // ========================================

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
            out long lastSentUnixTime))
        {
            return false;
        }

        long now =
            DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        long secondsSinceLastSend =
            now - lastSentUnixTime;

        return secondsSinceLastSend >= 0 &&
               secondsSinceLastSend < ResendCooldownSeconds;
    }

    // ========================================
    // LEGACY SESSION FOR DASHBOARD
    // ========================================

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

        // Fjerner blant annet metadata om
        // tidligere utsendte e-postkoder.

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
}
