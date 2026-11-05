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
/// Matching mellom behov og ressurser for offentlige aktører og admin.
///
/// "Finn ressurser"-siden fungerer uten AI: avstand og filtrering
/// gjøres av ResourceMatchingService. Sami bruker den samme tjenesten,
/// og forespørsler fra Sami-forslag sendes også hit (SendRequest).
/// </summary>
[Authorize(Roles = IdentitySeeder.PublicActorRole + "," + IdentitySeeder.AdminRole)]
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
    // FINN RESSURSER FOR ET BEHOV
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Need(
        int id,
        double radiusKm = ResourceMatchingService.DefaultRadiusKm
    )
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
                    excludeRequestedForNeedId: need.Id);

        List<AssignmentRowViewModel> assignments =
            await _context.NeedAssignments
                .AsNoTracking()
                .Where(a => a.NeedId == id)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new AssignmentRowViewModel
                {
                    Id = a.Id,
                    ResourceName = a.Resource!.ResourceName,
                    ResourceType = a.Resource.ResourceType,
                    Status = a.Status,
                    Message = a.Message,
                    SuggestedByAssistant = a.SuggestedByAssistant,
                    ContactEmail = a.Resource.ContactEmail ?? a.Resource.OwnerEmail,
                    ContactPhone =
                        a.Resource.ContactPhoneNumber == null
                            ? null
                            : a.Resource.ContactPhoneCountryCode + " " + a.Resource.ContactPhoneNumber,
                    CreatedAt = a.CreatedAt,
                    RespondedAt = a.RespondedAt
                })
                .ToListAsync();

        return View(new MatchNeedViewModel
        {
            Need = need,
            RadiusKm = Math.Clamp(radiusKm, 1, 500),
            Candidates = candidates,
            Assignments = assignments
        });
    }


    // =========================================================
    // SEND FORESPØRSEL TIL EIEREN AV EN RESSURS
    // =========================================================

    /// <summary>
    /// Brukes både av skjemaet på "Finn ressurser"-siden og av
    /// "Send forespørsel"-knappen på Samis forslag. Når kallet kommer
    /// fra Sami (fetch med Accept: application/json) svares det med JSON.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendRequest(
        int needId,
        int resourceId,
        string? message,
        bool suggestedByAssistant = false
    )
    {
        string userId = _userManager.GetUserId(User)!;

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
                suggestedByAssistant);

        if (WantsJson())
        {
            return Json(new { success = result.Success, message = result.Message });
        }

        TempData[result.Success ? "MatchMessage" : "MatchError"] = result.Message;

        return RedirectToAction(nameof(Need), new { id = needId });
    }


    // =========================================================
    // AVBRYT FORESPØRSEL
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int assignmentId, int needId)
    {
        ServiceResult result = await _assignments.CancelAsync(assignmentId);

        TempData[result.Success ? "MatchMessage" : "MatchError"] = result.Message;

        return RedirectToAction(nameof(Need), new { id = needId });
    }


    // =========================================================
    // MARKER SOM LØST
    // =========================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(int needId)
    {
        ServiceResult result = await _assignments.ResolveNeedAsync(needId);

        TempData[result.Success ? "MatchMessage" : "MatchError"] = result.Message;

        return RedirectToAction(nameof(Need), new { id = needId });
    }


    private bool WantsJson()
    {
        return HttpContext.Request.Headers.Accept
            .ToString()
            .Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }
}
