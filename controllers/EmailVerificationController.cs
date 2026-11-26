/*
    EmailVerificationController.cs – bekreftelse av e-post

    Hva filen gjør: Confirm: lenken i velkomst-e-posten. Bekrefter e-posten og slår på tofaktor
    samtidig. Status: spørres av «Sjekk e-posten din»-siden for å se om lenken er klikket.
    ConfirmChange: bekrefter ny e-postadresse.
    Hvorfor: da vet vi at adressen er ekte, og det trengs fordi varsler og koder sendes dit.

    På nettsiden: Siden man kommer til når man klikker lenken i e-posten.

    Oppgavekrav: 8, 14, 19
*/

using System.ComponentModel.DataAnnotations;
using System.Text;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FirstWebAppInDocker.Controllers;

/*
    Håndterer bekreftelse av e-postadresser, status for
    registrering og bekreftelse av endret e-postadresse.
    Bruker ASP.NET Core Identity til tokenvalidering.
*/
public class EmailVerificationController : Controller
{
    #region Felt og konstruktør

    // Må samsvare med nøkkelen i AccountController.
    private const string PendingRegistrationUserIdKey = "PendingRegistrationUserId";

    private readonly UserManager<ApplicationUser> _userManager;

    private readonly SignInManager<ApplicationUser> _signInManager;

    private readonly ApplicationDbContext _context;

    /*
        Oppretter kontrolleren med Identity og databasekonteksten.

        userManager:
        Administrerer brukere og Identity-tokener.

        signInManager:
        Administrerer autentiseringscookies.

        context:
        Databasekontekst for atomiske brukeroppdateringer.
    */
    public EmailVerificationController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext context)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _context = context;
    }

    #endregion

    #region Bekreft e-post ved registrering

    /*
        GET /EmailVerification/Confirm
        Validerer bekreftelseslenken og aktiverer e-postbasert
        tofaktorautentisering når e-postadressen er bekreftet.

        userId:
        Identifikatoren til brukeren som skal bekreftes.

        code:
        Base64Url-kodet bekreftelsestoken.

        Returnerer:
        Registreringsbekreftelse eller en feilmelding
        dersom lenken er ugyldig.
    */
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Confirm(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(code) ||
            userId.Length > 256 ||
            code.Length > 4096)
        {
            return BadRequest("Bekreftelseslenken er ugyldig.");
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return BadRequest("Bekreftelseslenken er ugyldig.");
        }

        bool alreadyConfirmed = await _userManager.IsEmailConfirmedAsync(user);

        /*
            Behandle gjentatte besøk på en bekreftelseslenke
            uten å starte en ny registreringsflyt.
        */
        if (alreadyConfirmed)
        {
            bool twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);

            if (!twoFactorEnabled)
            {
                /*
                    Dersom en tidligere bekreftelse ble avbrutt,
                    krev fortsatt et gyldig token før tofaktor
                    kan aktiveres.
                */
                string? existingToken = DecodeToken(code);

                if (existingToken is null)
                {
                    return BadRequest("Bekreftelseslenken er ugyldig.");
                }

                bool validToken =
                    await _userManager.VerifyUserTokenAsync(
                        user,
                        _userManager.Options.Tokens
                            .EmailConfirmationTokenProvider,
                        UserManager<ApplicationUser>
                            .ConfirmEmailTokenPurpose,
                        existingToken);

                if (!validToken)
                {
                    return BadRequest(
                        "Bekreftelseslenken er ugyldig " +
                        "eller utløpt. Kontakt administrator " +
                        "dersom kontoen ikke er aktivert.");
                }

                IdentityResult enableResult =
                    await _userManager.SetTwoFactorEnabledAsync(user, true);

                if (!enableResult.Succeeded)
                {
                    return Problem(
                        "E-postadressen er bekreftet, " +
                        "men tofaktorautentisering kunne " +
                        "ikke aktiveres. Kontakt administrator.");
                }
            }

            return ShowRegistrationCompleted(user.Id);
        }

        string? token = DecodeToken(code);

        if (token is null)
        {
            return BadRequest("Bekreftelseslenken er ugyldig.");
        }

        /*
            Identity og denne kontrolleren bruker samme
            scoped ApplicationDbContext. Transaksjonen
            hindrer at e-post bekreftes uten at tofaktor
            samtidig blir aktivert.
        */
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync();

        IdentityResult confirmationResult = await _userManager.ConfirmEmailAsync(user, token);

        if (!confirmationResult.Succeeded)
        {
            return BadRequest(
                "E-postadressen kunne ikke bekreftes. " +
                "Lenken kan være ugyldig eller utløpt.");
        }

        IdentityResult twoFactorResult = await _userManager.SetTwoFactorEnabledAsync(user, true);

        if (!twoFactorResult.Succeeded)
        {
            /*
                Transaksjonen rulles automatisk tilbake
                ved avslutning uten CommitAsync.
            */
            return Problem(
                "E-postadressen kunne ikke bekreftes " +
                "fullstendig fordi tofaktorautentisering " +
                "ikke kunne aktiveres. Prøv igjen senere.");
        }

        await transaction.CommitAsync();

        /*
            Selve sikkerhetskoden sendes senere
            fra TwoFactorController ved innlogging.
        */
        return ShowRegistrationCompleted(user.Id);
    }

    #endregion

    #region Registreringsstatus

    /*
        GET /EmailVerification/Status
        Returnerer registreringsstatus til JavaScript i
        RegistrationPending.cshtml.

        Returnerer:
        JSON med feltet confirmed, som er true når både
        e-postbekreftelse og tofaktor er aktivert.
    */
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Status()
    {
        /*
            Registreringssiden spør etter status fra
            samme sesjon som opprettet registreringen.
        */
        string? pendingUserId = HttpContext.Session.GetString(PendingRegistrationUserIdKey);

        if (string.IsNullOrWhiteSpace(pendingUserId))
        {
            return Json(new
            {
                confirmed = false
            });
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(pendingUserId);

        if (user is null)
        {
            return Json(new
            {
                confirmed = false
            });
        }

        bool emailConfirmed = await _userManager.IsEmailConfirmedAsync(user);

        bool twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);

        bool registrationCompleted = emailConfirmed && twoFactorEnabled;

        /*
            Returner bare status. Ingen token,
            e-postadresse eller personopplysninger.
        */
        return Json(new
        {
            confirmed = registrationCompleted
        });
    }

    /*
        Viser fullført registrering i samme nettlesersesjon
        eller direkte når bekreftelsen åpnes i en annen sesjon.

        userId:
        Brukeridentifikatoren til den bekreftede registreringen.

        Returnerer:
        Omdirigering til registreringssiden eller
        fullførtvisningen direkte.
    */
    private IActionResult ShowRegistrationCompleted(string userId)
    {
        string? pendingUserId = HttpContext.Session.GetString(PendingRegistrationUserIdKey);

        if (string.Equals(pendingUserId, userId, StringComparison.Ordinal))
        {
            return RedirectToAction("RegistrationPending", "Account");
        }

        /*
            Bekreftelseslenken kan åpnes i en annen
            nettleser uten tilgang til den opprinnelige
            registreringssesjonen.
        */
        ViewData["RegistrationComplete"] = true;

        return View("~/Views/Account/RegistrationPending.cshtml");
    }

    #endregion

    #region Bekreft ny e-postadresse

    /*
        GET /EmailVerification/ConfirmChange
        Validerer lenken for e-postendring og oppdaterer
        både e-postadresse og brukernavn gjennom Identity.

        userId:
        Brukeren som har bedt om å endre e-postadresse.

        newEmail:
        Den nye e-postadressen som skal bekreftes.

        code:
        Base64Url-kodet token for e-postendringen.

        Returnerer:
        En bekreftelse eller feilmelding.
    */
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> ConfirmChange(string? userId, string? newEmail, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(newEmail) ||
            string.IsNullOrWhiteSpace(code) ||
            userId.Length > 256 ||
            newEmail.Length > 320 ||
            code.Length > 4096)
        {
            return BadRequest("Bekreftelseslenken er ugyldig.");
        }

        newEmail = newEmail.Trim();

        EmailAddressAttribute emailValidator = new EmailAddressAttribute();

        if (!emailValidator.IsValid(newEmail))
        {
            return BadRequest("E-postadressen er ugyldig.");
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return BadRequest("Bekreftelseslenken er ugyldig.");
        }

        string? token = DecodeToken(code);

        if (token is null)
        {
            return BadRequest("Bekreftelseslenken er ugyldig.");
        }

        /*
            E-post og Identity-brukernavn skal være like
            i denne applikasjonen. Begge oppdateringene
            må derfor lykkes før transaksjonen lagres.
        */
        await using IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync();

        /*
            ChangeEmailAsync kontrollerer at tokenet er
            gyldig for brukeren og den nye adressen.
        */
        IdentityResult changeResult = await _userManager.ChangeEmailAsync(user, newEmail, token);

        if (!changeResult.Succeeded)
        {
            return BadRequest(
                "E-postadressen kunne ikke endres. " +
                "Lenken kan være ugyldig, utløpt, " +
                "eller adressen kan være i bruk.");
        }

        IdentityResult userNameResult = await _userManager.SetUserNameAsync(user, newEmail);

        if (!userNameResult.Succeeded)
        {
            return Problem(
                "E-postadressen kunne ikke oppdateres " +
                "fullstendig. Ingen endringer ble lagret. " +
                "Kontakt administrator dersom feilen vedvarer.");
        }

        bool twoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user);

        if (!twoFactorEnabled)
        {
            IdentityResult enableResult = await _userManager.SetTwoFactorEnabledAsync(user, true);

            if (!enableResult.Succeeded)
            {
                return Problem(
                    "E-postendringen kunne ikke fullføres " +
                    "fordi tofaktorautentisering ikke " +
                    "kunne aktiveres. Ingen endringer ble lagret.");
            }
        }

        await transaction.CommitAsync();

        /*
            En bekreftelseslenke kan åpnes mens en annen
            bruker er logget inn i samme nettleser.
            Logg derfor bare ut dersom den innloggede
            brukeren faktisk eier kontoen som ble endret.
        */
        string? signedInUserId = _userManager.GetUserId(User);

        if (User.Identity?.IsAuthenticated == true &&
            string.Equals(signedInUserId, user.Id, StringComparison.Ordinal))
        {
            await _signInManager.SignOutAsync();
            HttpContext.Session.Clear();
        }

        return Content(
            "Den nye e-postadressen er bekreftet!\n\n" +
            "E-postadressen på kontoen din " +
            "er nå oppdatert.\n\n" +
            "Du kan logge inn med den nye " +
            "e-postadressen og passordet ditt.\n\n" +
            "Du vil motta en sikkerhetskode " +
            "på den nye e-postadressen.",
            "text/plain; charset=utf-8");
    }

    #endregion

    #region Hjelpemetoder

    /*
        Dekoder et Base64Url-kodet Identity-token.

        encodedToken:
        Tokenet fra bekreftelseslenken.

        Returnerer:
        Det dekodede tokenet, eller null dersom
        verdien ikke er gyldig Base64Url.
    */
    private static string? DecodeToken(string encodedToken)
    {
        try
        {
            byte[] tokenBytes = WebEncoders.Base64UrlDecode(encodedToken);

            return Encoding.UTF8.GetString(tokenBytes);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    #endregion
}
