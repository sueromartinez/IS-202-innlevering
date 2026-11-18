
using System.ComponentModel.DataAnnotations;
using System.Text;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer bekreftelse av e-postadresser, status for
/// registrering og bekreftelse av endret e-postadresse.
/// Bruker ASP.NET Core Identity til tokenvalidering.
/// </summary>
public class EmailVerificationController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _dbContext;

    // Må samsvare med nøkkelen i AccountController.
    private const string PendingRegistrationUserIdKey =
        "PendingRegistrationUserId";

    /// <summary>
    /// Oppretter kontrolleren med Identity og databasekonteksten.
    /// </summary>
    /// <param name="userManager">
    /// Administrerer brukere og Identity-tokener.
    /// </param>
    /// <param name="signInManager">
    /// Administrerer autentiseringscookies.
    /// </param>
    /// <param name="dbContext">
    /// Databasekontekst for atomiske brukeroppdateringer.
    /// </param>
    public EmailVerificationController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext dbContext)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _dbContext = dbContext;
    }

    #region Bekreftelse av e-post ved registrering

    /// <summary>
    /// Validerer bekreftelseslenken og aktiverer e-postbasert
    /// tofaktorautentisering når e-postadressen er bekreftet.
    /// </summary>
    /// <param name="userId">
    /// Identifikatoren til brukeren som skal bekreftes.
    /// </param>
    /// <param name="code">
    /// Base64Url-kodet bekreftelsestoken.
    /// </param>
    /// <returns>
    /// Registreringsbekreftelse eller en feilmelding
    /// dersom lenken er ugyldig.
    /// </returns>
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> Confirm(
        string? userId,
        string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(code) ||
            userId.Length > 256 ||
            code.Length > 4096)
        {
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        ApplicationUser? user =
            await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        bool alreadyConfirmed =
            await _userManager.IsEmailConfirmedAsync(user);

        // Behandle gjentatte besøk på en bekreftelseslenke
        // uten å starte en ny registreringsflyt.
        if (alreadyConfirmed)
        {
            bool twoFactorEnabled =
                await _userManager.GetTwoFactorEnabledAsync(user);

            if (!twoFactorEnabled)
            {
                // Dersom en tidligere bekreftelse ble avbrutt,
                // krev fortsatt et gyldig token før tofaktor
                // kan aktiveres.
                string? existingToken = DecodeToken(code);

                if (existingToken is null)
                {
                    return BadRequest(
                        "Bekreftelseslenken er ugyldig.");
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
                    await _userManager.SetTwoFactorEnabledAsync(
                        user,
                        true);

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
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        // Identity og denne kontrolleren bruker samme
        // scoped ApplicationDbContext. Transaksjonen
        // hindrer at e-post bekreftes uten at tofaktor
        // samtidig blir aktivert.
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        IdentityResult confirmationResult =
            await _userManager.ConfirmEmailAsync(
                user,
                token);

        if (!confirmationResult.Succeeded)
        {
            return BadRequest(
                "E-postadressen kunne ikke bekreftes. " +
                "Lenken kan være ugyldig eller utløpt.");
        }

        IdentityResult twoFactorResult =
            await _userManager.SetTwoFactorEnabledAsync(
                user,
                true);

        if (!twoFactorResult.Succeeded)
        {
            // Transaksjonen rulles automatisk tilbake
            // ved avslutning uten CommitAsync.
            return Problem(
                "E-postadressen kunne ikke bekreftes " +
                "fullstendig fordi tofaktorautentisering " +
                "ikke kunne aktiveres. Prøv igjen senere.");
        }

        await transaction.CommitAsync();

        // Selve sikkerhetskoden sendes senere
        // fra TwoFactorController ved innlogging.
        return ShowRegistrationCompleted(user.Id);
    }

    #endregion

    #region Registreringsstatus

    /// <summary>
    /// Returnerer registreringsstatus til JavaScript i
    /// RegistrationPending.cshtml.
    /// </summary>
    /// <returns>
    /// JSON med feltet confirmed, som er true når både
    /// e-postbekreftelse og tofaktor er aktivert.
    /// </returns>
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> Status()
    {
        // Registreringssiden spør etter status fra
        // samme sesjon som opprettet registreringen.
        string? pendingUserId =
            HttpContext.Session.GetString(
                PendingRegistrationUserIdKey);

        if (string.IsNullOrWhiteSpace(pendingUserId))
        {
            return Json(new
            {
                confirmed = false
            });
        }

        ApplicationUser? user =
            await _userManager.FindByIdAsync(
                pendingUserId);

        if (user is null)
        {
            return Json(new
            {
                confirmed = false
            });
        }

        bool emailConfirmed =
            await _userManager.IsEmailConfirmedAsync(user);

        bool twoFactorEnabled =
            await _userManager.GetTwoFactorEnabledAsync(user);

        bool registrationCompleted =
            emailConfirmed && twoFactorEnabled;

        // Returner bare status. Ingen token,
        // e-postadresse eller personopplysninger.
        return Json(new
        {
            confirmed = registrationCompleted
        });
    }

    /// <summary>
    /// Viser fullført registrering i samme nettlesersesjon
    /// eller direkte når bekreftelsen åpnes i en annen sesjon.
    /// </summary>
    /// <param name="userId">
    /// Brukeridentifikatoren til den bekreftede registreringen.
    /// </param>
    /// <returns>
    /// Omdirigering til registreringssiden eller
    /// fullførtvisningen direkte.
    /// </returns>
    private IActionResult ShowRegistrationCompleted(
        string userId)
    {
        string? pendingUserId =
            HttpContext.Session.GetString(
                PendingRegistrationUserIdKey);

        if (string.Equals(
            pendingUserId,
            userId,
            StringComparison.Ordinal))
        {
            return RedirectToAction(
                "RegistrationPending",
                "Account");
        }

        // Bekreftelseslenken kan åpnes i en annen
        // nettleser uten tilgang til den opprinnelige
        // registreringssesjonen.
        ViewData["RegistrationComplete"] = true;

        return View(
            "~/Views/Account/RegistrationPending.cshtml");
    }

    #endregion

    #region Bekreftelse av endret e-postadresse

    /// <summary>
    /// Validerer lenken for e-postendring og oppdaterer
    /// både e-postadresse og brukernavn gjennom Identity.
    /// </summary>
    /// <param name="userId">
    /// Brukeren som har bedt om å endre e-postadresse.
    /// </param>
    /// <param name="newEmail">
    /// Den nye e-postadressen som skal bekreftes.
    /// </param>
    /// <param name="code">
    /// Base64Url-kodet token for e-postendringen.
    /// </param>
    /// <returns>
    /// En bekreftelse eller feilmelding.
    /// </returns>
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> ConfirmChange(
        string? userId,
        string? newEmail,
        string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(newEmail) ||
            string.IsNullOrWhiteSpace(code) ||
            userId.Length > 256 ||
            newEmail.Length > 320 ||
            code.Length > 4096)
        {
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        newEmail = newEmail.Trim();

        var emailValidator = new EmailAddressAttribute();

        if (!emailValidator.IsValid(newEmail))
        {
            return BadRequest(
                "E-postadressen er ugyldig.");
        }

        ApplicationUser? user =
            await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        string? token = DecodeToken(code);

        if (token is null)
        {
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        // E-post og Identity-brukernavn skal være like
        // i denne applikasjonen. Begge oppdateringene
        // må derfor lykkes før transaksjonen lagres.
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        // ChangeEmailAsync kontrollerer at tokenet er
        // gyldig for brukeren og den nye adressen.
        IdentityResult changeResult =
            await _userManager.ChangeEmailAsync(
                user,
                newEmail,
                token);

        if (!changeResult.Succeeded)
        {
            return BadRequest(
                "E-postadressen kunne ikke endres. " +
                "Lenken kan være ugyldig, utløpt, " +
                "eller adressen kan være i bruk.");
        }

        IdentityResult userNameResult =
            await _userManager.SetUserNameAsync(
                user,
                newEmail);

        if (!userNameResult.Succeeded)
        {
            return Problem(
                "E-postadressen kunne ikke oppdateres " +
                "fullstendig. Ingen endringer ble lagret. " +
                "Kontakt administrator dersom feilen vedvarer.");
        }

        bool twoFactorEnabled =
            await _userManager.GetTwoFactorEnabledAsync(user);

        if (!twoFactorEnabled)
        {
            IdentityResult enableResult =
                await _userManager.SetTwoFactorEnabledAsync(
                    user,
                    true);

            if (!enableResult.Succeeded)
            {
                return Problem(
                    "E-postendringen kunne ikke fullføres " +
                    "fordi tofaktorautentisering ikke " +
                    "kunne aktiveres. Ingen endringer ble lagret.");
            }
        }

        await transaction.CommitAsync();

        // En bekreftelseslenke kan åpnes mens en annen
        // bruker er logget inn i samme nettleser.
        // Logg derfor bare ut dersom den innloggede
        // brukeren faktisk eier kontoen som ble endret.
        string? signedInUserId =
            _userManager.GetUserId(User);

        if (User.Identity?.IsAuthenticated == true &&
            string.Equals(
                signedInUserId,
                user.Id,
                StringComparison.Ordinal))
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

    /// <summary>
    /// Dekoder et Base64Url-kodet Identity-token.
    /// </summary>
    /// <param name="encodedToken">
    /// Tokenet fra bekreftelseslenken.
    /// </param>
    /// <returns>
    /// Det dekodede tokenet, eller null dersom
    /// verdien ikke er gyldig Base64Url.
    /// </returns>
    private static string? DecodeToken(
        string encodedToken)
    {
        try
        {
            byte[] tokenBytes =
                WebEncoders.Base64UrlDecode(
                    encodedToken);

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
