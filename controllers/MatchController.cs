
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Matching;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Matching og koordinering for offentlige aktører og administratorer.
///
/// Koordinering samler:
/// - Behov som trenger matching
/// - Forespørsler som venter på svar
/// - Bekreftede ressurser
/// - Historikk over løste behov
///
/// Eksisterende matching, forespørsler og tildelinger beholdes.
/// </summary>
[Authorize(
    Roles = IdentitySeeder.PublicActorRole + ","
          + IdentitySeeder.AdminRole
)]
public class MatchController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ResourceMatchingService _matching;
    private readonly AssignmentService _assignments;
    private readonly UserManager<ApplicationUser> _userManager;

    public MatchController(
        ApplicationDbContext context,
        ResourceMatchingService matching,
        AssignmentService assignments,
        UserManager<ApplicationUser> userManager
    )
    {
        _context = context;
        _matching = matching;
        _assignments = assignments;
        _userManager = userManager;
    }

    // =========================================================
    // KOORDINERING – KANBAN-OVERSIKT
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // ========================================
        // HENT ALLE BEHOV
        // ========================================

        List<Need> allNeeds =
            await _context.Needs
                .AsNoTracking()
                .OrderByDescending(need => need.Id)
                .ToListAsync();

        // Aktive behov skal vises på Kanban-tavlen.
        // Løste behov skal vises under Historikk.

        List<Need> activeNeeds =
            allNeeds
                .Where(need =>
                    need.Status != NeedStatus.Resolved)
                .ToList();

        List<Need> resolvedNeeds =
            allNeeds
                .Where(need =>
                    need.Status == NeedStatus.Resolved)
                .ToList();

        // ========================================
        // HENT STATUS PÅ FORESPØRSLENE
        // ========================================

        // Vi trenger ikke å hente hele Resource-objektet
        // her. Vi trenger bare informasjonen som avgjør
        // hvilken kolonne et behov tilhører.

        var assignmentStates =
            await _context.NeedAssignments
                .AsNoTracking()
                .Select(assignment => new
                {
                    assignment.NeedId,
                    assignment.Status,
                    assignment.RespondedAt
                })
                .ToListAsync();

        // ========================================
        // GRUPPER ETTER BEHOV
        // ========================================

        var groupedAssignments =
            assignmentStates
                .GroupBy(assignment =>
                    assignment.NeedId)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList()
                );

        // ========================================
        // TELLEVERDIER FOR HVER BEHOVS-ID
        // ========================================

        var pendingByNeed =
            new Dictionary<int, int>();

        var confirmedByNeed =
            new Dictionary<int, int>();

        var totalByNeed =
            new Dictionary<int, int>();

        foreach (Need need in allNeeds)
        {
            int pending = 0;
            int confirmed = 0;
            int total = 0;

            if (groupedAssignments.TryGetValue(
                need.Id,
                out var relatedAssignments))
            {
                foreach (var assignment in relatedAssignments)
                {
                    total++;

                    string status =
                        assignment.Status.ToString();

                    // Ressursleverandøren har godtatt
                    // forespørselen.

                    bool isConfirmed =
                        status.Equals(
                            "Accepted",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        status.Equals(
                            "Confirmed",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        status.Equals(
                            "Approved",
                            StringComparison.OrdinalIgnoreCase);

                    // Avslåtte og avbrutte forespørsler
                    // skal ikke telle som ventende.

                    bool isClosedWithoutConfirmation =
                        status.Equals(
                            "Rejected",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        status.Equals(
                            "Declined",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        status.Equals(
                            "Cancelled",
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        status.Equals(
                            "Canceled",
                            StringComparison.OrdinalIgnoreCase);

                    if (isConfirmed)
                    {
                        confirmed++;
                    }
                    else if (
                        !isClosedWithoutConfirmation
                        && !assignment.RespondedAt.HasValue)
                    {
                        pending++;
                    }
                }
            }

            pendingByNeed[need.Id] = pending;
            confirmedByNeed[need.Id] = confirmed;
            totalByNeed[need.Id] = total;
        }

        // ========================================
        // DATA TIL KANBAN-VISNINGEN
        // ========================================

        // Vi beholder List<Need> som modell.
        //
        // Da fungerer den eksisterende Index.cshtml
        // frem til vi erstatter den med Kanban-designet.

        ViewBag.PendingByNeed = pendingByNeed;
        ViewBag.ConfirmedByNeed = confirmedByNeed;
        ViewBag.TotalByNeed = totalByNeed;

        ViewBag.ResolvedNeeds = resolvedNeeds;

        // ========================================
        // STATISTIKK FOR AKTIVE BEHOV
        // ========================================

        ViewBag.TotalActiveNeeds = activeNeeds.Count;

        ViewBag.TotalPending =
            activeNeeds.Sum(need =>
                pendingByNeed[need.Id]);

        ViewBag.TotalConfirmed =
            activeNeeds.Sum(need =>
                confirmedByNeed[need.Id]);

        ViewBag.TotalResolvedNeeds =
            resolvedNeeds.Count;

        // ========================================
        // RETURNER KOORDINERING-SIDEN
        // ========================================

        return View(activeNeeds);
    }

    // =========================================================
    // TILDELINGER – EKSISTERENDE OVERSIKT
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Assignments()
    {
        var assignmentRows =
            await _context.NeedAssignments
                .AsNoTracking()
                .OrderByDescending(
                    assignment => assignment.CreatedAt)
                .Select(assignment => new
                {
                    NeedId = assignment.NeedId,

                    Row = new AssignmentRowViewModel
                    {
                        Id = assignment.Id,

                        ResourceName =
                            assignment.Resource!.ResourceName,

                        ResourceType =
                            assignment.Resource.ResourceType,

                        Status = assignment.Status,

                        Message = assignment.Message,

                        SuggestedByAssistant =
                            assignment.SuggestedByAssistant,

                        ContactEmail =
                            assignment.Resource.ContactEmail
                            ?? assignment.Resource.OwnerEmail,

                        ContactPhone =
                            assignment.Resource.ContactPhoneNumber == null
                                ? null
                                : assignment.Resource.ContactPhoneCountryCode
                                  + " "
                                  + assignment.Resource.ContactPhoneNumber,

                        CreatedAt = assignment.CreatedAt,

                        RespondedAt = assignment.RespondedAt
                    }
                })
                .ToListAsync();

        var model = new List<MatchNeedViewModel>();

        if (assignmentRows.Count == 0)
        {
            return View(model);
        }

        List<int> needIds =
            assignmentRows
                .Select(item => item.NeedId)
                .Distinct()
                .ToList();

        List<Need> needs =
            await _context.Needs
                .AsNoTracking()
                .Where(need =>
                    needIds.Contains(need.Id))
                .OrderByDescending(need => need.Id)
                .ToListAsync();

        var assignmentsByNeed =
            assignmentRows
                .GroupBy(item => item.NeedId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(item => item.Row)
                        .ToList()
                );

        foreach (Need need in needs)
        {
            List<AssignmentRowViewModel> rows =
                assignmentsByNeed.TryGetValue(
                    need.Id,
                    out List<AssignmentRowViewModel>? found)
                    ? found
                    : new List<AssignmentRowViewModel>();

            model.Add(new MatchNeedViewModel
            {
                Need = need,

                RadiusKm =
                    ResourceMatchingService.DefaultRadiusKm,

                Candidates =
                    new List<ResourceCandidate>(),

                Assignments = rows
            });
        }

        return View(model);
    }

    // =========================================================
    // FINN RESSURSER FOR ET KONKRET BEHOV
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Need(
        int id,
        double radiusKm =
            ResourceMatchingService.DefaultRadiusKm)
    {
        Need? need =
            await _context.Needs
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == id);

        if (need is null)
        {
            return NotFound();
        }

        List<ResourceCandidate> candidates =
            need.Status == NeedStatus.Resolved
                ? new List<ResourceCandidate>()
                : await _matching.FindNearbyResourcesAsync(
                    need.Latitude,
                    need.Longitude,
                    radiusKm,
                    new[] { need.ResourceType },
                    maxResults: 15,
                    excludeRequestedForNeedId: need.Id
                );

        List<AssignmentRowViewModel> assignments =
            await _context.NeedAssignments
                .AsNoTracking()
                .Where(a => a.NeedId == id)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new AssignmentRowViewModel
                {
                    Id = a.Id,

                    ResourceName =
                        a.Resource!.ResourceName,

                    ResourceType =
                        a.Resource.ResourceType,

                    Status = a.Status,

                    Message = a.Message,

                    SuggestedByAssistant =
                        a.SuggestedByAssistant,

                    ContactEmail =
                        a.Resource.ContactEmail
                        ?? a.Resource.OwnerEmail,

                    ContactPhone =
                        a.Resource.ContactPhoneNumber == null
                            ? null
                            : a.Resource.ContactPhoneCountryCode
                              + " "
                              + a.Resource.ContactPhoneNumber,

                    CreatedAt = a.CreatedAt,

                    RespondedAt = a.RespondedAt
                })
                .ToListAsync();

        return View(new MatchNeedViewModel
        {
            Need = need,

            RadiusKm =
                Math.Clamp(radiusKm, 1, 500),

            Candidates = candidates,

            Assignments = assignments
        });
    }

    // =========================================================
    // SEND FORESPØRSEL
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendRequest(
        int needId,
        int resourceId,
        string? message,
        bool suggestedByAssistant = false)
    {
        string userId =
            _userManager.GetUserId(User)!;

        if (message is { Length: > 500 })
        {
            message = message[..500];
        }

        ServiceResult result =
            await _assignments.RequestAsync(
                needId,
                resourceId,
                userId,
                message,
                suggestedByAssistant
            );

        if (WantsJson())
        {
            return Json(new
            {
                success = result.Success,
                message = result.Message
            });
        }

        TempData[
            result.Success
                ? "MatchMessage"
                : "MatchError"
        ] = result.Message;

        return RedirectToAction(
            nameof(Need),
            new { id = needId }
        );
    }

    // =========================================================
    // AVBRYT FORESPØRSEL
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int assignmentId,
        int needId)
    {
        ServiceResult result =
            await _assignments.CancelAsync(
                assignmentId
            );

        TempData[
            result.Success
                ? "MatchMessage"
                : "MatchError"
        ] = result.Message;

        return RedirectToAction(
            nameof(Need),
            new { id = needId }
        );
    }

    // =========================================================
    // MARKER BEHOV SOM LØST
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(
        int needId)
    {
        ServiceResult result =
            await _assignments.ResolveNeedAsync(
                needId
            );

        TempData[
            result.Success
                ? "MatchMessage"
                : "MatchError"
        ] = result.Message;

        return RedirectToAction(
            nameof(Need),
            new { id = needId }
        );
    }

    // =========================================================
    // JSON-SVAR TIL SAMI
    // =========================================================

    private bool WantsJson()
    {
        return HttpContext.Request.Headers.Accept
            .ToString()
            .Contains(
                "application/json",
                StringComparison.OrdinalIgnoreCase
            );
    }
}
