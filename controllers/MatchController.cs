
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.Services.Matching;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

[Authorize(
    Roles = IdentitySeeder.PublicActorRole + "," +
            IdentitySeeder.AdminRole
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
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _matching = matching;
        _assignments = assignments;
        _userManager = userManager;
    }

    // =========================================================
    // KOORDINERING – KANBAN-TAVLE
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        List<Need> allNeeds = await _context.Needs
            .AsNoTracking()
            .OrderByDescending(n => n.Id)
            .ToListAsync();

        List<Need> activeNeeds = allNeeds
            .Where(n => n.Status != NeedStatus.Resolved)
            .ToList();

        List<Need> resolvedNeeds = allNeeds
            .Where(n => n.Status == NeedStatus.Resolved)
            .ToList();

        // Bare aktive forespørsler skal brukes til
        // å plassere behov på Kanban-tavlen.
        //
        // Cancelled og Declined regnes ikke
        // som aktive forespørsler.

        var activeAssignmentStates =
            await _context.NeedAssignments
                .AsNoTracking()
                .Where(a =>
                    a.Status == AssignmentStatus.Requested ||
                    a.Status == AssignmentStatus.Confirmed)
                .Select(a => new
                {
                    a.NeedId,
                    a.Status
                })
                .ToListAsync();

        var groupedAssignments = activeAssignmentStates
            .GroupBy(a => a.NeedId)
            .ToDictionary(
                group => group.Key,
                group => group.ToList()
            );

        var pendingByNeed = new Dictionary<int, int>();
        var confirmedByNeed = new Dictionary<int, int>();
        var totalByNeed = new Dictionary<int, int>();

        foreach (Need need in allNeeds)
        {
            int pending = 0;
            int confirmed = 0;

            if (groupedAssignments.TryGetValue(
                need.Id,
                out var relatedAssignments))
            {
                pending = relatedAssignments.Count(a =>
                    a.Status == AssignmentStatus.Requested);

                confirmed = relatedAssignments.Count(a =>
                    a.Status == AssignmentStatus.Confirmed);
            }

            pendingByNeed[need.Id] = pending;
            confirmedByNeed[need.Id] = confirmed;
            totalByNeed[need.Id] = pending + confirmed;
        }

        ViewBag.PendingByNeed = pendingByNeed;
        ViewBag.ConfirmedByNeed = confirmedByNeed;
        ViewBag.TotalByNeed = totalByNeed;
        ViewBag.ResolvedNeeds = resolvedNeeds;

        ViewBag.TotalActiveNeeds = activeNeeds.Count;

        ViewBag.TotalPending = activeNeeds.Sum(
            n => pendingByNeed[n.Id]);

        ViewBag.TotalConfirmed = activeNeeds.Sum(
            n => confirmedByNeed[n.Id]);

        ViewBag.TotalResolvedNeeds = resolvedNeeds.Count;

        return View(activeNeeds);
    }

    // =========================================================
    // TILDELINGER – OVERSIKT
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Assignments()
    {
        // Avbrutte forespørsler vises ikke
        // i den aktive tildelingsoversikten.
        //
        // De beholdes fortsatt i databasen.

        var assignmentRows =
            await _context.NeedAssignments
                .AsNoTracking()
                .Where(a =>
                    a.Status != AssignmentStatus.Cancelled)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    NeedId = a.NeedId,

                    Row = new AssignmentRowViewModel
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
                    }
                })
                .ToListAsync();

        var model = new List<MatchNeedViewModel>();

        if (assignmentRows.Count == 0)
        {
            return View(model);
        }

        List<int> needIds = assignmentRows
            .Select(a => a.NeedId)
            .Distinct()
            .ToList();

        List<Need> needs = await _context.Needs
            .AsNoTracking()
            .Where(n => needIds.Contains(n.Id))
            .OrderByDescending(n => n.Id)
            .ToListAsync();

        var assignmentsByNeed = assignmentRows
            .GroupBy(a => a.NeedId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(a => a.Row)
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

                Candidates = new List<ResourceCandidate>(),
                Assignments = rows
            });
        }

        return View(model);
    }

    // =========================================================
    // MATCHING FOR ET KONKRET BEHOV
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Need(
        int id,
        double radiusKm =
            ResourceMatchingService.DefaultRadiusKm)
    {
        Need? need = await _context.Needs
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id);

        if (need is null)
        {
            return NotFound();
        }

        // Eksisterende matching beholdes.

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

        // NYTT:
        // Avbrutte forespørsler fjernes fra
        // listen som vises på detaljsiden.
        //
        // Bekreftede, ventende og avslåtte
        // forespørsler kan fortsatt vises.
        //
        // Ingen historikk slettes fra databasen.

        List<AssignmentRowViewModel> assignments =
            await _context.NeedAssignments
                .AsNoTracking()
                .Where(a =>
                    a.NeedId == id &&
                    a.Status != AssignmentStatus.Cancelled)
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

        // Hastegrad for behovet (samme modell som i behovslisten).
        ViewData["Urgency"] =
            (await new NeedUrgencyService(_context).CalculateAsync(new[] { need }))
                .GetValueOrDefault(need.Id);

        return View(new MatchNeedViewModel
        {
            Need = need,
            RadiusKm = Math.Clamp(radiusKm, 1, 500),
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
        string userId = _userManager.GetUserId(User)!;

        if (message is { Length: > 500 })
        {
            message = message[..500];
        }

        ServiceResult result = await _assignments.RequestAsync(
            needId,
            resourceId,
            userId,
            message,
            suggestedByAssistant
        );

        if (result.Success)
        {
            await this.AuditAsync(
                AuditActions.RequestSent,
                $"Forespørsel om ressurs #{resourceId} til behov #{needId}"
                + (suggestedByAssistant ? " (foreslått av Sami)" : ""));
        }

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
            await _assignments.CancelAsync(assignmentId);

        if (result.Success)
        {
            await this.AuditAsync(
                AuditActions.RequestCancelled,
                $"Forespørsel #{assignmentId} på behov #{needId} ble avbrutt");
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
    // MARKER BEHOV SOM LØST
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(
        int needId)
    {
        ServiceResult result =
            await _assignments.ResolveNeedAsync(needId);

        if (result.Success)
        {
            await this.AuditAsync(
                AuditActions.NeedResolved,
                $"Behov #{needId} markert som løst");
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
