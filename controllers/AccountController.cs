using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Midlertidig controller for innlogging, utlogging og profil.
/// Skal senere erstattes med ASP.NET Core Identity.
/// </summary>
public class AccountController : Controller
{
    private const string PublicActorEmail =
        "offentlig@beredskap.no";

    private const string ResourceProviderEmail =
        "privat@beredskap.no";

    private const string TestPassword =
        "Test123!";


    /// <summary>
    /// Logger inn en midlertidig testbruker.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(
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
            model.Email
                ?.Trim()
                .ToLowerInvariant()
            ?? string.Empty;


        /*
         * Offentlig aktør / beredskapsbruker
         */
        if (
            email ==
            PublicActorEmail.ToLowerInvariant()
            &&
            model.Password ==
            TestPassword
        )
        {
            CreateSession(
                PublicActorEmail,
                "Beredskapsbruker",
                "PublicActor"
            );

            return RedirectToAction(
                "Dashboard",
                "Home"
            );
        }


        /*
         * Ressursleverandør
         */
        if (
            email ==
            ResourceProviderEmail.ToLowerInvariant()
            &&
            model.Password ==
            TestPassword
        )
        {
            CreateSession(
                ResourceProviderEmail,
                "Ressursleverandør",
                "ResourceProvider"
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


    /// <summary>
    /// Direkte testinnlogging for offentlig aktør.
    /// Brukes av knappen på forsiden.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DemoPublicActorLogin()
    {
        CreateSession(
            PublicActorEmail,
            "Beredskapsbruker",
            "PublicActor"
        );

        return RedirectToAction(
            "Dashboard",
            "Home"
        );
    }


    /// <summary>
    /// Logger ut brukeren.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();

        return RedirectToAction(
            "Index",
            "Home"
        );
    }


    /// <summary>
    /// Viser profilen til innlogget bruker.
    /// </summary>
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


        return View(
            model
        );
    }


    /// <summary>
    /// Oppdaterer midlertidig profilinformasjon.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Profile(
        ProfileViewModel model
    )
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


        if (!ModelState.IsValid)
        {
            return View(
                model
            );
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


    /// <summary>
    /// Oppretter Session-data
    /// for en midlertidig bruker.
    /// </summary>
    private void CreateSession(
        string email,
        string name,
        string role
    )
    {
        /*
         * Fjern eventuell gammel session først.
         */
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


        /*
         * Midlertidig standard
         * kontaktinformasjon.
         */
        HttpContext.Session.SetString(
            "ContactEmail",
            email
        );


        HttpContext.Session.SetString(
            "PhoneCountryCode",
            "+47"
        );
    }
}