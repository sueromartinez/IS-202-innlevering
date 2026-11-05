using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Varsellisten bak varselklokken. Alle innloggede brukere har en.
/// </summary>
[Authorize]
public class NotificationController : Controller
{
    private readonly ApplicationDbContext _context;

    private readonly UserManager<ApplicationUser> _userManager;


    public NotificationController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager
    )
    {
        _context = context;
        _userManager = userManager;
    }


    /// <summary>
    /// Viser de siste varslene. Uleste varsler vises uthevet
    /// én gang, og markeres deretter som lest.
    /// </summary>
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

        // Lager en kopi av lest-status før den endres,
        // slik at visningen kan utheve de som var nye.
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
}
