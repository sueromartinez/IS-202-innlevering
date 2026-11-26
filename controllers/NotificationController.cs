/*
    NotificationController.cs – varsler

    Hva filen gjør: Viser den innloggede brukerens varsler og markerer dem som lest.

    På nettsiden: Siden «Varsler». Nye varsler er uthevet første gang de vises. Tallet på klokka
    kommer fra NotificationBellViewComponent.

    Oppgavekrav: 10, 18
*/

using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

// Varsellisten bak varselklokken. Alle innloggede brukere har en.
[Authorize]
public class NotificationController : Controller
{
    #region Felt og konstruktør

    private readonly ApplicationDbContext _context;

    private readonly UserManager<ApplicationUser> _userManager;

    public NotificationController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    #endregion

    #region Varsler

    /*
        GET /Notification
        Viser de siste varslene. Uleste varsler vises uthevet
        én gang, og markeres deretter som lest.
    */
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        string userId = _userManager.GetUserId(User)!;

        List<Notification> notifications =
            await _context.Notifications
                .Where(notification => notification.UserId == userId)
                .OrderByDescending(notification => notification.CreatedAt)
                .Take(50)
                .ToListAsync();

        /*
            Lager en kopi av lest-status før den endres,
            slik at visningen kan utheve de som var nye.
        */
        HashSet<int> unreadIds =
            notifications
                .Where(notification => !notification.IsRead)
                .Select(notification => notification.Id)
                .ToHashSet();

        foreach (Notification notification in notifications)
        {
            notification.IsRead = true;
        }

        await _context.SaveChangesAsync();

        ViewBag.UnreadIds = unreadIds;

        return View(notifications);
    }

    #endregion
}
