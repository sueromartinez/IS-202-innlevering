using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.Services.Matching;
using FirstWebAppInDocker.Services.Notifications;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Sider for frivillige / ressursleverandører:
/// - Forespørsler: se og svare på forespørsler om egne ressurser
/// - Behov jeg kan hjelpe med: åpne behov som passer egne ressurser
///
/// Frivillige ser aldri andres ressurser, og ser bare den
/// informasjonen om behov som trengs for å vurdere om de kan hjelpe.
/// </summary>
[Authorize(Roles = IdentitySeeder.ResourceProviderRole)]
public class RequestController : Controller
{
    private readonly ApplicationDbContext _context;

    private readonly AssignmentService _assignments;

    private readonly ResourceMatchingService _matching;

    private readonly NotificationService _notifications;

    private readonly UserManager<ApplicationUser> _userManager;


    public RequestController(
        ApplicationDbContext context,
        AssignmentService assignments,
        ResourceMatchingService matching,
        NotificationService notifications,
        UserManager<ApplicationUser> userManager
    )
    {
        _context = context;
        _assignments = assignments;
        _matching = matching;
        _notifications = notifications;
        _userManager = userManager;
    }


    // =========================================================
    // MINE FORESPØRSLER
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        string userId = _userManager.GetUserId(User)!;

        var assignments =
            await _context.NeedAssignments
                .AsNoTracking()
                .Include(a => a.Need)
                .Include(a => a.Resource)
                .Where(a => a.Resource!.OwnerUserId == userId)
                .OrderBy(a => a.Status == AssignmentStatus.Requested ? 0 : 1)
                .ThenByDescending(a => a.CreatedAt)
                .Take(100)
                .ToListAsync();

        // Kontaktinfo til den offentlige aktøren vises bare
        // på forespørsler den frivillige har bekreftet.
        List<string> requesterIds =
            assignments
                .Where(a => a.Status == AssignmentStatus.Confirmed)
                .Select(a => a.RequestedByUserId)
                .Distinct()
                .ToList();

        Dictionary<string, string> requesterContacts =
            await _context.Users
                .Where(user => requesterIds.Contains(user.Id))
                .ToDictionaryAsync(
                    user => user.Id,
                    user => $"{user.Name} ({user.Email})");

        List<VolunteerRequestViewModel> model =
            assignments
                .Select(a => new VolunteerRequestViewModel
                {
                    Id = a.Id,
                    ResourceName = a.Resource!.ResourceName,
                    NeedType = a.Need!.ResourceType,
                    NeedPriority = a.Need.Priority,
                    NeedDescription = a.Need.Description,
                    NeedQuantity = a.Need.Quantity,
                    NeedDeadline = a.Need.Deadline,
                    DistanceKm = Math.Round(
                        GeoDistance.HaversineKm(
                            a.Need.Latitude,
                            a.Need.Longitude,
                            a.Resource.Latitude,
                            a.Resource.Longitude),
                        1),
                    Status = a.Status,
                    Message = a.Message,
                    RequesterContact =
                        a.Status == AssignmentStatus.Confirmed
                            // Kontaktpunktet på behovet brukes hvis det er fylt ut.
                            ? (!string.IsNullOrWhiteSpace(a.Need.ContactName) || !string.IsNullOrWhiteSpace(a.Need.ContactPhone)
                                ? $"{a.Need.ContactName} {a.Need.ContactPhone}".Trim()
                                : requesterContacts.GetValueOrDefault(a.RequestedByUserId))
                            : null,
                    CreatedAt = a.CreatedAt
                })
                .ToList();

        return View(model);
    }


    // =========================================================
    // SVAR PÅ FORESPØRSEL
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Respond(int id, bool accept)
    {
        string userId = _userManager.GetUserId(User)!;

        ServiceResult result =
            await _assignments.RespondAsync(id, userId, accept);

        if (result.Success)
        {
            await this.AuditAsync(
                AuditActions.RequestAnswered,
                $"Forespørsel #{id} ble {(accept ? "bekreftet" : "avslått")}");
        }

        TempData[result.Success ? "RequestMessage" : "RequestError"] = result.Message;

        return RedirectToAction(nameof(Index));
    }


    // =========================================================
    // BEHOV JEG KAN HJELPE MED
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> MatchingNeeds(
        double radiusKm = ResourceMatchingService.DefaultRadiusKm
    )
    {
        string userId = _userManager.GetUserId(User)!;

        bool hasAvailableResources =
            await _context.Resources.AnyAsync(resource =>
                resource.OwnerUserId == userId
                && resource.Status == ResourceStatus.Available);

        List<NeedCandidate> needs =
            await _matching.FindNeedsForProviderAsync(userId, radiusKm, maxResults: 25);

        return View(new MatchingNeedsViewModel
        {
            RadiusKm = Math.Clamp(radiusKm, 1, 500),
            HasAvailableResources = hasAvailableResources,
            Needs = needs
        });
    }


    /// <summary>
    /// Den frivillige sier fra at de kan hjelpe med et behov.
    /// Den offentlige aktøren som registrerte behovet får varsel
    /// og kan sende en formell forespørsel fra "Finn ressurser".
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OfferHelp(int needId, int resourceId)
    {
        string userId = _userManager.GetUserId(User)!;

        Resource? resource =
            await _context.Resources
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == resourceId && r.OwnerUserId == userId);

        Need? need =
            await _context.Needs
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == needId);

        if (resource is null || need is null || need.Status == NeedStatus.Resolved)
        {
            TempData["RequestError"] = "Kunne ikke melde interesse for dette behovet.";
            return RedirectToAction(nameof(MatchingNeeds));
        }

        // Nye behov har OwnerUserId, eldre behov bare e-post.
        ApplicationUser? needOwner =
            need.OwnerUserId is not null
                ? await _userManager.FindByIdAsync(need.OwnerUserId)
                : await _userManager.FindByEmailAsync(need.OwnerEmail);

        if (needOwner is not null)
        {
            await _notifications.NotifyAsync(
                needOwner.Id,
                $"Tilbud om hjelp: {resource.ResourceName}",
                $"En frivillig tilbyr «{resource.ResourceName}» ({EnumDisplay.Name(resource.ResourceType)}) "
                + $"til behovet «{need.Description}». Send en forespørsel fra Finn ressurser for å avtale.",
                $"/Match/Need/{need.Id}");
        }

        TempData["RequestMessage"] =
            "Takk! Den offentlige aktøren har fått beskjed og kan sende deg en forespørsel.";

        return RedirectToAction(nameof(MatchingNeeds));
    }
}
