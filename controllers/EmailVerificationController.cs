
using System.ComponentModel.DataAnnotations;
using System.Text;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace FirstWebAppInDocker.Controllers;

public class EmailVerificationController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    // Må være samme nøkkel som i AccountController.
    private const string PendingRegistrationUserIdKey =
        "PendingRegistrationUserId";

    public EmailVerificationController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    // ========================================
    // BEKREFT E-POST VED REGISTRERING
    // ========================================

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> Confirm(
        string? userId,
        string? code)
    {
        // Kontroller parameterne i lenken.

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(code) ||
            userId.Length > 256 ||
            code.Length > 4096)
        {
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        // ========================================
        // FINN BRUKER
        // ========================================

        ApplicationUser? user =
            await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        // ========================================
        // KONTROLLER EKSISTERENDE BEKREFTELSE
        // ========================================
        //
        // Hvis brukeren allerede har bekreftet
        // e-postadressen, trenger ikke Identity
        // bekrefte samme adresse på nytt.
        //
        // Vi kontrollerer også at tofaktor
        // faktisk er aktivert.

        bool alreadyConfirmed =
            await _userManager.IsEmailConfirmedAsync(user);

        if (alreadyConfirmed)
        {
            bool twoFactorEnabled =
                await _userManager.GetTwoFactorEnabledAsync(
                    user);

            if (!twoFactorEnabled)
            {
                return Problem(
                    "E-postadressen er allerede bekreftet, " +
                    "men tofaktorautentisering er ikke " +
                    "aktivert. Kontakt administrator.");
            }

            return ShowRegistrationCompleted(user.Id);
        }

        // ========================================
        // DEKOD BEKREFTELSESTOKEN
        // ========================================

        string? token = DecodeToken(code);

        if (token is null)
        {
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        // ========================================
        // BEKREFT E-POST MED IDENTITY
        // ========================================

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

        // ========================================
        // AKTIVER E-POSTBASERT TOFAKTOR
        // ========================================
        //
        // Nye brukere skal bruke e-postkode
        // ved hver nye innlogging.
        //
        // Vi aktiverer Identity sin
        // tofaktorfunksjonalitet.
        //
        // Selve e-postkoden sendes senere
        // av TwoFactorController.

        IdentityResult twoFactorResult =
            await _userManager.SetTwoFactorEnabledAsync(
                user,
                true);

        if (!twoFactorResult.Succeeded)
        {
            return Problem(
                "E-postadressen ble bekreftet, men " +
                "tofaktorautentisering kunne ikke " +
                "aktiveres. Kontakt administrator.");
        }

        // ========================================
        // VIS REGISTRERING FULLFØRT
        // ========================================

        return ShowRegistrationCompleted(user.Id);
    }

    // ========================================
    // NYTT: STATUS FOR REGISTRERING
    // ========================================
    //
    // Popupen på RegistrationPending.cshtml
    // spør denne handlingen hvert fjerde sekund.
    //
    // Vi returnerer bare en boolsk verdi.
    //
    // Ingen e-postadresse, token eller
    // personopplysninger returneres.
    //
    // Brukeren trenger ikke være logget inn.
    // Vi bruker den midlertidige sesjonen
    // som ble opprettet ved registreringen.

    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> Status()
    {
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

        // ========================================
        // FINN REGISTRERINGEN
        // ========================================

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

        // ========================================
        // KONTROLLER BEKREFTELSE OG TOFAKTOR
        // ========================================
        //
        // Begge må være fullført før popupen
        // kan vise "Registrering fullført".

        bool emailConfirmed =
            await _userManager.IsEmailConfirmedAsync(
                user);

        bool twoFactorEnabled =
            await _userManager.GetTwoFactorEnabledAsync(
                user);

        bool registrationCompleted =
            emailConfirmed && twoFactorEnabled;

        // ========================================
        // RETURNER STATUS TIL JAVASCRIPT
        // ========================================
        //
        // Eksempel før bekreftelse:
        //
        // { "confirmed": false }
        //
        // Eksempel etter bekreftelse:
        //
        // { "confirmed": true }
        //
        // RegistrationPending.cshtml bruker
        // denne verdien til å bytte popup.

        return Json(new
        {
            confirmed = registrationCompleted
        });
    }

    // ========================================
    // NYTT: VIS FULLFØRT-POPUP
    // ========================================
    //
    // Når bekreftelseslenken åpnes:
    //
    // Samme nettleser med aktiv registrering:
    //  -> Gå til RegistrationPending.
    //
    // Annen nettleser eller annen sesjon:
    //  -> Vis fullført-popupen direkte.
    //
    // Dermed fungerer bekreftelseslenken
    // også når den åpnes fra en annen fane
    // eller nettleser.

    private IActionResult ShowRegistrationCompleted(
        string userId)
    {
        string? pendingUserId =
            HttpContext.Session.GetString(
                PendingRegistrationUserIdKey);

        // ========================================
        // SAMME REGISTRERINGSSESJON
        // ========================================

        if (string.Equals(
            pendingUserId,
            userId,
            StringComparison.Ordinal))
        {
            return RedirectToAction(
                "RegistrationPending",
                "Account");
        }

        // ========================================
        // ANNEN SESJON
        // ========================================
        //
        // Vi kan ikke anta at denne nettleseren
        // har samme Session som registreringen.
        //
        // Derfor viser vi Razor-siden direkte
        // i fullført-tilstand.
        //
        // Dette oppretter ingen innlogging.

        ViewData["RegistrationComplete"] = true;

        return View(
            "~/Views/Account/RegistrationPending.cshtml");
    }

    // ========================================
    // BEKREFT ENDRING AV E-POSTADRESSE
    // ========================================
    //
    // Denne handlingen beholder vi fra
    // den tidligere løsningen.
    //
    // Ved endring av e-post på profilsiden
    // skal brukeren bekrefte den nye adressen
    // før den gamle erstattes.

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
        // ========================================
        // VALIDER PARAMETERE
        // ========================================

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

        var emailValidator = new EmailAddressAttribute();

        if (!emailValidator.IsValid(newEmail))
        {
            return BadRequest(
                "E-postadressen er ugyldig.");
        }

        // ========================================
        // FINN BRUKER
        // ========================================

        ApplicationUser? user =
            await _userManager.FindByIdAsync(
                userId);

        if (user is null)
        {
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        // ========================================
        // KONTROLLER AT ADRESSEN ER LEDIG
        // ========================================

        ApplicationUser? existingUser =
            await _userManager.FindByEmailAsync(
                newEmail);

        if (existingUser is not null &&
            existingUser.Id != user.Id)
        {
            return BadRequest(
                "Denne e-postadressen er allerede i bruk.");
        }

        // Vi bruker e-post som brukernavn,
        // så dette må også kontrolleres.

        ApplicationUser? existingUserName =
            await _userManager.FindByNameAsync(
                newEmail);

        if (existingUserName is not null &&
            existingUserName.Id != user.Id)
        {
            return BadRequest(
                "Denne e-postadressen kan ikke brukes.");
        }

        // ========================================
        // DEKOD TOKEN
        // ========================================

        string? token = DecodeToken(code);

        if (token is null)
        {
            return BadRequest(
                "Bekreftelseslenken er ugyldig.");
        }

        // ========================================
        // BEKREFT NY E-POSTADRESSE
        // ========================================
        //
        // ChangeEmailAsync verifiserer at
        // tokenet gjelder denne brukeren
        // og den nye e-postadressen.
        //
        // Først etter gyldig token blir
        // adressen endret.

        IdentityResult changeResult =
            await _userManager.ChangeEmailAsync(
                user,
                newEmail,
                token);

        if (!changeResult.Succeeded)
        {
            return BadRequest(
                "E-postadressen kunne ikke endres. " +
                "Lenken kan være ugyldig eller utløpt.");
        }

        // ========================================
        // OPPDATER BRUKERNAVN
        // ========================================
        //
        // I dette prosjektet er Identity sitt
        // brukernavn lik e-postadressen.

        IdentityResult userNameResult =
            await _userManager.SetUserNameAsync(
                user,
                newEmail);

        if (!userNameResult.Succeeded)
        {
            return Problem(
                "Den nye e-postadressen ble bekreftet, " +
                "men brukernavnet kunne ikke oppdateres. " +
                "Kontakt administrator.");
        }

        // ========================================
        // BEHOLD TOFAKTOR
        // ========================================

        bool twoFactorEnabled =
            await _userManager.GetTwoFactorEnabledAsync(
                user);

        if (!twoFactorEnabled)
        {
            IdentityResult enableResult =
                await _userManager.SetTwoFactorEnabledAsync(
                    user,
                    true);

            if (!enableResult.Succeeded)
            {
                return Problem(
                    "E-postadressen ble endret, men " +
                    "tofaktor kunne ikke aktiveres. " +
                    "Kontakt administrator.");
            }
        }

        // ========================================
        // AVSLUTT INNLOGGING I DENNE NETTLESEREN
        // ========================================
        //
        // Brukeren skal logge inn på nytt
        // med den oppdaterte e-postadressen.

        await _signInManager.SignOutAsync();

        HttpContext.Session.Clear();

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

    // ========================================
    // HJELPEMETODE - DEKOD TOKEN
    // ========================================

    private static string? DecodeToken(
        string encodedToken)
    {
        try
        {
            byte[] tokenBytes =
                WebEncoders.Base64UrlDecode(
                    encodedToken);

            return Encoding.UTF8.GetString(
                tokenBytes);
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
}
