using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer midlertidig innlogging, utlogging og profil.
/// Skal senere erstattes med ASP.NET Core Identity.
/// </summary>
public class AccountController : Controller
{
    /// <summary>
    /// Logger inn en bruker basert på testbrukere.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        /*
         * Midlertidig offentlig testbruker.
         */
        if (
            model.Email == "offentlig@beredskap.no"
            &&
            model.Password == "Test123!"
        )
        {
            HttpContext.Session.SetString(
                "UserEmail",
                model.Email
            );

            HttpContext.Session.SetString(
                "UserName",
                "Offentlig testbruker"
            );

            HttpContext.Session.SetString(
                "UserRole",
                "PublicActor"
            );


            return RedirectToAction(
                "Dashboard",
                "Home"
            );
        }


        /*
         * Midlertidig ressursleverandør.
         */
        if (
            model.Email == "privat@beredskap.no"
            &&
            model.Password == "Test123!"
        )
        {
            HttpContext.Session.SetString(
                "UserEmail",
                model.Email
            );

            HttpContext.Session.SetString(
                "UserName",
                "Ressursleverandør"
            );

            HttpContext.Session.SetString(
                "UserRole",
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
    /// Logger ut brukeren ved å tømme Session.
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
    /// Viser profilsiden til innlogget bruker.
    /// </summary>
    [HttpGet]
    public IActionResult Profile()
    {
        string? userEmail =
            HttpContext.Session.GetString(
                "UserEmail"
            );

        string? userName =
            HttpContext.Session.GetString(
                "UserName"
            );


        if (string.IsNullOrEmpty(userEmail))
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
                    userName
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


        return View(model);
    }


    /// <summary>
    /// Oppdaterer kontaktinformasjon på profilsiden.
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


        if (string.IsNullOrEmpty(userEmail))
        {
            return RedirectToAction(
                "Index",
                "Home"
            );
        }


        if (!ModelState.IsValid)
        {
            return View(model);
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
}