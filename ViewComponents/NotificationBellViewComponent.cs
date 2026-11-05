using System.Security.Claims;
using FirstWebAppInDocker.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.ViewComponents;

/// <summary>
/// Viser varselklokken i menyen med antall uleste varsler.
/// Brukes fra _TopNavigation og andre headere med
/// @await Component.InvokeAsync("NotificationBell").
/// </summary>
public class NotificationBellViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;


    public NotificationBellViewComponent(
        ApplicationDbContext context
    )
    {
        _context = context;
    }


    public async Task<IViewComponentResult> InvokeAsync()
    {
        string? userId =
            UserClaimsPrincipal.FindFirstValue(
                ClaimTypes.NameIdentifier
            );


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
}
