/*
    NotificationBellViewComponent.cs – varselklokka

    Hva filen gjør: Teller uleste varsler for den innloggede og tegner klokka. En View Component
    er som en liten controller med egen visning, som kan legges inn i menyen på alle sider.

    På nettsiden: Tallet på klokka i menyen.

    Oppgavekrav: 19
*/

using System.Security.Claims;
using FirstWebAppInDocker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.ViewComponents;

/*
    Viser varselklokken i menyen med antall uleste varsler.
    Brukes fra _TopNavigation og andre headere med
    @await Component.InvokeAsync("NotificationBell").
*/
public class NotificationBellViewComponent : ViewComponent
{
    #region Felt og konstruktør

    private readonly ApplicationDbContext _context;

    public NotificationBellViewComponent(ApplicationDbContext context)
    {
        _context = context;
    }

    #endregion

    #region Varselklokke

    public async Task<IViewComponentResult> InvokeAsync()
    {
        string? userId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);

        int unreadCount = 0;

        if (!string.IsNullOrEmpty(userId))
        {
            unreadCount =
                await _context.Notifications
                    .CountAsync(notification =>
                        notification.UserId == userId
                        && !notification.IsRead);
        }

        return View(unreadCount);
    }

    #endregion
}
