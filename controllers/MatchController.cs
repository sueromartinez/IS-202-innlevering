
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

/// <summary>
/// Håndterer koordinering, ressursmatching og tildelinger
/// for offentlige aktører og administratorer.
/// </summary>
/// <remarks>
/// Innlogging og roller kontrolleres med ASP.NET Core Identity.
///
/// Offentlige aktører kan se koordineringsoversikten,
/// men kan bare endre forespørsler på behov de selv eier.
/// Administratorer kan administrere alle behov.
///
/// Permanente eierskapskontroller bruker OwnerUserId.
/// OwnerEmail støttes for eldre behov uten Identity-eier.
/// </remarks>
[Authorize(
    Roles = IdentitySeeder.PublicActorRole + "," +
            IdentitySeeder.AdminRole)]
public class MatchController : Controller
{
    #region Konstanter og avhengigheter

    private const double MinRadiusKm = 1;
    private const double MaxRadiusKm = 500;

    private readonly ApplicationDbContext _context;
    private readonly ResourceMatchingService _matching;
    private readonly AssignmentService _assignments;
    private readonly UserManager<ApplicationUser> _userManager;

    #endregion

    #region Konstruktør

    /// <summary>
    /// Oppretter kontrolleren med database-, matching-,
    /// tildelings- og Identity-tjenester.
    /// </summary>
    /// <param name="context">Applikasjonens databasekontekst.</param>
    /// <param name="matching">Tjeneste for ressursmatching.</param>
    /// <param name="assignments">Tjeneste for forespørsler og tildelinger.</param>
    /// <param name="userManager">Identity-tjeneste for brukere.</param>
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

    #endregion

    #region Koordinering og Kanban-tavle

    /// <summary>
    /// Viser aktive og løste behov samt antall aktive
    /// forespørsler og bekreftelser per behov.
    /// </summary>
    /// <returns>Kanban-tavlen med eksisterende ViewBag-data.</returns>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        List<Need> allNeeds = await _context.Needs
            .AsNoTracking()
            .OrderByDescending(need => need.Id)
            .ToListAsync();

        List<Need> activeNeeds = allNeeds
            .Where(need => need.Status != NeedStatus.Resolved)
            .ToList();

        List<Need> resolvedNeeds = allNeeds
            .Where(need => need.Status == NeedStatus.Resolved)
            .ToList();

        // Bare Requested og Confirmed skal telle
        // som aktive forespørsler på Kanban-tavlen.
        var activeAssignmentStates =
            await _context.NeedAssignments
                .AsNoTracking()
                .Where(assignment =>
                    assignment.Status == AssignmentStatus.Requested
                    || assignment.Status == AssignmentStatus.Confirmed)
                .Select(assignment => new
                {
                    assignment.NeedId,
                    assignment.Status
                })
                .ToListAsync();

        var groupedAssignments = activeAssignmentStates
            .GroupBy(assignment => assignment.NeedId)
            .ToDictionary(
                group => group.Key,
                group => group.ToList());

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
                pending = relatedAssignments.Count(assignment =>
                    assignment.Status == AssignmentStatus.Requested);

                confirmed = relatedAssignments.Count(assignment =>
                    assignment.Status == AssignmentStatus.Confirmed);
            }

            pendingByNeed[need.Id] = pending;
            confirmedByNeed[need.Id] = confirmed;
            totalByNeed[need.Id] = pending + confirmed;
        }

        // Behold ViewBag-navnene som Razor-visningen bruker.
        ViewBag.PendingByNeed = pendingByNeed;
        ViewBag.ConfirmedByNeed = confirmedByNeed;
        ViewBag.TotalByNeed = totalByNeed;
        ViewBag.ResolvedNeeds = resolvedNeeds;

        ViewBag.TotalActiveNeeds = activeNeeds.Count;
        ViewBag.TotalPending = activeNeeds.Sum(
            need => pendingByNeed[need.Id]);

        ViewBag.TotalConfirmed = activeNeeds.Sum(
            need => confirmedByNeed[need.Id]);

        ViewBag.TotalResolvedNeeds = resolvedNeeds.Count;

        return View(activeNeeds);
    }

    #endregion

    #region Tildelingsoversikt

    /// <summary>
    /// Viser registrerte forespørsler og tildelinger,
    /// gruppert etter behov.
    /// </summary>
    /// <remarks>
    /// Kansellerte forespørsler skjules fra oversikten,
    /// men beholdes i MariaDB for historikk og sporbarhet.
    /// </remarks>
    /// <returns>En liste med MatchNeedViewModel.</returns>
    [HttpGet]
    public async Task<IActionResult> Assignments()
    {
        var assignmentRows =
            await _context.NeedAssignments
                .AsNoTracking()
                .Where(assignment =>
                    assignment.Status != AssignmentStatus.Cancelled)
                .OrderByDescending(assignment => assignment.CreatedAt)
                .Select(assignment => new
                {
                    assignment.NeedId,

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

        List<int> needIds = assignmentRows
            .Select(assignment => assignment.NeedId)
            .Distinct()
            .ToList();

        List<Need> needs = await _context.Needs
            .AsNoTracking()
            .Where(need => needIds.Contains(need.Id))
            .OrderByDescending(need => need.Id)
            .ToListAsync();

        var assignmentsByNeed = assignmentRows
            .GroupBy(assignment => assignment.NeedId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(assignment => assignment.Row)
                    .ToList());

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
                RadiusKm = ResourceMatchingService.DefaultRadiusKm,
                Candidates = new List<ResourceCandidate>(),
                Assignments = rows
            });
        }

        return View(model);
    }

    #endregion

    #region Ressursmatching for et behov

    /// <summary>
    /// Viser et behov og finner tilgjengelige ressurser
    /// som passer behovets kategori og geografiske plassering.
    /// </summary>
    /// <param name="id">Behovets identifikator.</param>
    /// <param name="radiusKm">Søkeradius i kilometer.</param>
    /// <returns>Detaljsiden for behovet med ressurskandidater.</returns>
    [HttpGet]
    public async Task<IActionResult> Need(
        int id,
        double radiusKm = ResourceMatchingService.DefaultRadiusKm)
    {
        Need? need = await _context.Needs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id);

        if (need is null)
        {
            return NotFound();
        }

        radiusKm = NormalizeRadius(radiusKm);

        Coordinate needPosition = need.Position;

        // Løste behov skal ikke foreslå nye ressurser.
        List<ResourceCandidate> candidates =
            need.Status == NeedStatus.Resolved
                ? new List<ResourceCandidate>()
                : await _matching.FindNearbyResourcesAsync(
                    needPosition.Latitude,
                    needPosition.Longitude,
                    radiusKm,
                    new[] { need.ResourceType },
                    maxResults: 15,
                    excludeRequestedForNeedId: need.Id);

        // Kartposisjonene ligger utenfor ResourceCandidate
        // fordi denne returtypen også brukes av Sami.
        List<int> candidateIds = candidates
            .Select(candidate => candidate.ResourceId)
            .ToList();

        ViewData["CandidatePositions"] =
            await _context.Resources
                .AsNoTracking()
                .Where(resource => candidateIds.Contains(resource.Id))
                .ToDictionaryAsync(
                    resource => resource.Id,
                    resource => new[]
                    {
                        resource.Latitude,
                        resource.Longitude
                    });

        // Kansellerte forespørsler skal ikke vises,
        // men historikken beholdes i databasen.
        List<AssignmentRowViewModel> assignments =
            await _context.NeedAssignments
                .AsNoTracking()
                .Where(assignment =>
                    assignment.NeedId == id
                    && assignment.Status != AssignmentStatus.Cancelled)
                .OrderByDescending(assignment => assignment.CreatedAt)
                .Select(assignment => new AssignmentRowViewModel
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
                })
                .ToListAsync();

        return View(new MatchNeedViewModel
        {
            Need = need,
            RadiusKm = radiusKm,
            Candidates = candidates,
            Assignments = assignments
        });
    }

    #endregion

    #region Sende forespørsel

    /// <summary>
    /// Sender en forespørsel om en ressurs til et behov
    /// som den innloggede brukeren har lov til å administrere.
    /// </summary>
    /// <param name="needId">Behovets identifikator.</param>
    /// <param name="resourceId">Ressursens identifikator.</param>
    /// <param name="message">Valgfri melding til ressurseieren.</param>
    /// <param name="suggestedByAssistant">
    /// Angir om ressursen er foreslått av Sami.
    /// </param>
    /// <returns>JSON eller omdirigering til behovets detaljside.</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendRequest(
        int needId,
        int resourceId,
        string? message,
        bool suggestedByAssistant = false)
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        Need? need = await _context.Needs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == needId);

        if (need is null)
        {
            return NotFound();
        }

        // En offentlig aktør skal bare administrere
        // sine egne behov. Admin kan administrere alle.
        if (!await CanManageNeedAsync(need, userId))
        {
            return Forbid();
        }

        if (message is { Length: > 500 })
        {
            message = message[..500];
        }

        ServiceResult result = await _assignments.RequestAsync(
            needId,
            resourceId,
            userId,
            message,
            suggestedByAssistant);

        if (result.Success)
        {
            await this.AuditAsync(
                AuditActions.RequestSent,
                $"Forespørsel om ressurs #{resourceId} til behov #{needId}"
                + (suggestedByAssistant ? " (foreslått av Sami)" : ""));
        }

        // Behold JSON-responsen som Sami og AJAX-kall bruker.
        if (WantsJson())
        {
            return Json(new
            {
                success = result.Success,
                message = result.Message
            });
        }

        TempData[
            result.Success ? "MatchMessage" : "MatchError"
        ] = result.Message;

        return RedirectToAction(
            nameof(Need),
            new { id = needId });
    }

    #endregion

    #region Avbryte forespørsel

    /// <summary>
    /// Avbryter en forespørsel dersom brukeren eier
    /// behovet eller har administratorrollen.
    /// </summary>
    /// <remarks>
    /// Identifikatorene kontrolleres mot databasen før
    /// AssignmentService.CancelAsync kalles.
    ///
    /// Kansellering endrer forespørselens status, men
    /// sletter ikke forespørselen fra databasen.
    /// </remarks>
    /// <param name="assignmentId">Forespørselens identifikator.</param>
    /// <param name="needId">Behovet forespørselen tilhører.</param>
    /// <returns>Omdirigering til behovets detaljside.</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(
        int assignmentId,
        int needId)
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        // Kontroller at forespørselen faktisk tilhører
        // behovet som ble sendt inn fra skjemaet.
        NeedAssignment? assignment =
            await _context.NeedAssignments
                .AsNoTracking()
                .Include(item => item.Need)
                .FirstOrDefaultAsync(item =>
                    item.Id == assignmentId
                    && item.NeedId == needId);

        if (assignment?.Need is null)
        {
            return NotFound();
        }

        // Stopp handlingen før CancelAsync dersom
        // brukeren ikke har eierskap til behovet.
        if (!await CanManageNeedAsync(assignment.Need, userId))
        {
            return Forbid();
        }

        ServiceResult result =
            await _assignments.CancelAsync(assignment.Id);

        if (result.Success)
        {
            await this.AuditAsync(
                AuditActions.RequestCancelled,
                $"Forespørsel #{assignment.Id} " +
                $"på behov #{assignment.NeedId} ble avbrutt");
        }

        TempData[
            result.Success ? "MatchMessage" : "MatchError"
        ] = result.Message;

        // Bruk verifisert NeedId fra databasen,
        // ikke en ubekreftet ID fra skjemaet.
        return RedirectToAction(
            nameof(Need),
            new { id = assignment.NeedId });
    }

    #endregion

    #region Markere behov som løst

    /// <summary>
    /// Marker et behov som løst og frigjør ressursene
    /// som ikke lenger er i bruk.
    /// </summary>
    /// <param name="needId">Behovets identifikator.</param>
    /// <returns>Omdirigering til behovets detaljside.</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolve(int needId)
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        Need? need = await _context.Needs
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == needId);

        if (need is null)
        {
            return NotFound();
        }

        // Bare behovets eier eller administrator
        // skal kunne markere behovet som løst.
        if (!await CanManageNeedAsync(need, userId))
        {
            return Forbid();
        }

        ServiceResult result =
            await _assignments.ResolveNeedAsync(need.Id);

        if (result.Success)
        {
            await this.AuditAsync(
                AuditActions.NeedResolved,
                $"Behov #{need.Id} markert som løst");
        }

        TempData[
            result.Success ? "MatchMessage" : "MatchError"
        ] = result.Message;

        return RedirectToAction(
            nameof(Need),
            new { id = need.Id });
    }

    #endregion

    #region Autorisasjon og hjelpemetoder

    /// <summary>
    /// Kontrollerer om den innloggede brukeren har rett
    /// til å administrere et bestemt behov.
    /// </summary>
    /// <remarks>
    /// Administratorer kan administrere alle behov.
    ///
    /// For andre brukere brukes OwnerUserId når den finnes.
    /// OwnerEmail brukes bare for eldre behov uten
    /// permanent Identity-eier.
    /// </remarks>
    /// <param name="need">Behovet som skal administreres.</param>
    /// <param name="userId">Innlogget brukers Identity-ID.</param>
    /// <returns>
    /// True dersom brukeren er administrator eller eier behovet.
    /// </returns>
    private async Task<bool> CanManageNeedAsync(
        Need need,
        string userId)
    {
        if (User.IsInRole(IdentitySeeder.AdminRole))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(need.OwnerUserId))
        {
            return string.Equals(
                need.OwnerUserId,
                userId,
                StringComparison.Ordinal);
        }

        // Støtt eldre behov som bare har OwnerEmail.
        // Ikke bruk e-post som reserve dersom
        // OwnerUserId allerede er registrert.
        if (string.IsNullOrWhiteSpace(need.OwnerEmail))
        {
            return false;
        }

        ApplicationUser? currentUser =
            await _userManager.GetUserAsync(User);

        if (currentUser is null
            || string.IsNullOrWhiteSpace(currentUser.Email))
        {
            return false;
        }

        return string.Equals(
            currentUser.Email,
            need.OwnerEmail,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Begrens søkeradius til portalens tillatte intervall.
    /// </summary>
    /// <param name="radiusKm">Ønsket radius i kilometer.</param>
    /// <returns>En gyldig radius.</returns>
    private static double NormalizeRadius(double radiusKm)
    {
        if (!double.IsFinite(radiusKm))
        {
            return ResourceMatchingService.DefaultRadiusKm;
        }

        return Math.Clamp(
            radiusKm,
            MinRadiusKm,
            MaxRadiusKm);
    }

    /// <summary>
    /// Kontrollerer om forespørselen ønsker JSON-respons.
    /// </summary>
    /// <returns>True dersom Accept-headeren inneholder application/json.</returns>
    private bool WantsJson()
    {
        return HttpContext.Request.Headers.Accept
            .ToString()
            .Contains(
                "application/json",
                StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
