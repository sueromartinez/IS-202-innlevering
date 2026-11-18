
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
/// Håndterer forespørsler og matchende behov for
/// ressursleverandører i Beredskapsportalen.
/// </summary>
/// <remarks>
/// Ressursleverandører kan se forespørsler knyttet til
/// egne ressurser, svare på forespørsler og tilby hjelp
/// til offentlige aktørers åpne behov.
///
/// Tilgang krever innlogging med ResourceProvider-rollen.
/// Eierskap kontrolleres i tillegg for hver handling.
/// </remarks>
[Authorize(Roles = IdentitySeeder.ResourceProviderRole)]
public class RequestController : Controller
{
    #region Konstanter og avhengigheter

    private const double MinRadiusKm = 1;
    private const double MaxRadiusKm = 500;

    private readonly ApplicationDbContext _context;
    private readonly AssignmentService _assignments;
    private readonly ResourceMatchingService _matching;
    private readonly NotificationService _notifications;
    private readonly UserManager<ApplicationUser> _userManager;

    #endregion

    #region Konstruktør

    /// <summary>
    /// Oppretter kontrolleren med nødvendige tjenester.
    /// </summary>
    /// <param name="context">
    /// Databasekontekst for behov, ressurser og forespørsler.
    /// </param>
    /// <param name="assignments">
    /// Tjeneste for håndtering av forespørsler og tildelinger.
    /// </param>
    /// <param name="matching">
    /// Tjeneste for geografisk matching av behov og ressurser.
    /// </param>
    /// <param name="notifications">
    /// Tjeneste for varsling av brukere.
    /// </param>
    /// <param name="userManager">
    /// Identity-tjeneste for innloggede brukere.
    /// </param>
    public RequestController(
        ApplicationDbContext context,
        AssignmentService assignments,
        ResourceMatchingService matching,
        NotificationService notifications,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _assignments = assignments;
        _matching = matching;
        _notifications = notifications;
        _userManager = userManager;
    }

    #endregion

    #region Mine forespørsler

    /// <summary>
    /// Viser forespørsler knyttet til ressursene
    /// som tilhører den innloggede ressursleverandøren.
    /// </summary>
    /// <remarks>
    /// Kansellerte forespørsler utelates fra listen.
    /// De beholdes i databasen av historikkhensyn.
    ///
    /// Kontaktinformasjon til den offentlige aktøren
    /// vises bare for bekreftede forespørsler.
    /// </remarks>
    /// <returns>
    /// En oversikt over ressursleverandørens forespørsler.
    /// </returns>
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> Index()
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        // Hent kun forespørsler for ressurser som
        // tilhører den innloggede brukeren.
        //
        // Kansellerte forespørsler skal ikke vises
        // som aktive eller ubesvarte forespørsler.
        List<NeedAssignment> assignments =
            await _context.NeedAssignments
                .AsNoTracking()
                .Include(a => a.Need)
                .Include(a => a.Resource)
                .Where(a =>
                    a.Resource!.OwnerUserId == userId
                    && a.Status != AssignmentStatus.Cancelled)
                .OrderBy(a =>
                    a.Status == AssignmentStatus.Requested ? 0 : 1)
                .ThenByDescending(a => a.CreatedAt)
                .Take(100)
                .ToListAsync();

        // Kontaktinformasjon skal bare hentes for
        // forespørsler som faktisk er bekreftet.
        List<string> requesterIds = assignments
            .Where(a =>
                a.Status == AssignmentStatus.Confirmed)
            .Select(a => a.RequestedByUserId)
            .Distinct()
            .ToList();

        Dictionary<string, string> requesterContacts =
            await _context.Users
                .AsNoTracking()
                .Where(user =>
                    requesterIds.Contains(user.Id))
                .ToDictionaryAsync(
                    user => user.Id,
                    user => $"{user.Name} ({user.Email})");

        // Bygg eksisterende ViewModel slik at
        // Razor-visningen fortsatt fungerer.
        List<VolunteerRequestViewModel> model =
            assignments
                .Select(a =>
                {
                    Need need = a.Need!;
                    Resource resource = a.Resource!;

                    Coordinate needPosition = need.Position;
                    Coordinate resourcePosition = resource.Position;

                    double distanceKm =
                        GeoDistance.HaversineKm(
                            needPosition.Latitude,
                            needPosition.Longitude,
                            resourcePosition.Latitude,
                            resourcePosition.Longitude);

                    string? requesterContact = null;

                    if (a.Status == AssignmentStatus.Confirmed)
                    {
                        // Foretrekk behovets registrerte
                        // kontaktperson når informasjon finnes.
                        if (!string.IsNullOrWhiteSpace(need.ContactName)
                            || !string.IsNullOrWhiteSpace(need.ContactPhone))
                        {
                            requesterContact =
                                $"{need.ContactName} {need.ContactPhone}"
                                    .Trim();
                        }
                        else
                        {
                            requesterContact =
                                requesterContacts.GetValueOrDefault(
                                    a.RequestedByUserId);
                        }
                    }

                    return new VolunteerRequestViewModel
                    {
                        Id = a.Id,
                        ResourceName = resource.ResourceName,

                        NeedType = need.ResourceType,
                        NeedPriority = need.Priority,
                        NeedDescription = need.Description,
                        NeedQuantity = need.Quantity,
                        NeedDeadline = need.Deadline,

                        DistanceKm = Math.Round(distanceKm, 1),

                        Status = a.Status,
                        Message = a.Message,
                        RequesterContact = requesterContact,
                        CreatedAt = a.CreatedAt
                    };
                })
                .ToList();

        return View(model);
    }

    #endregion

    #region Besvare forespørsler

    /// <summary>
    /// Lar ressursleverandøren bekrefte eller avslå
    /// en forespørsel om en av sine egne ressurser.
    /// </summary>
    /// <param name="id">
    /// Identifikatoren til forespørselen.
    /// </param>
    /// <param name="accept">
    /// True for å bekrefte, false for å avslå.
    /// </param>
    /// <returns>
    /// Omdirigering til oversikten over forespørsler.
    /// </returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Respond(int id, bool accept)
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        // AssignmentService kontrollerer også at
        // brukeren eier ressursen før status endres.
        ServiceResult result =
            await _assignments.RespondAsync(
                id,
                userId,
                accept);

        if (result.Success)
        {
            await this.AuditAsync(
                AuditActions.RequestAnswered,
                $"Forespørsel #{id} ble " +
                (accept ? "bekreftet" : "avslått"));
        }

        TempData[
            result.Success ? "RequestMessage" : "RequestError"
        ] = result.Message;

        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Behov jeg kan hjelpe med

    /// <summary>
    /// Viser åpne behov som passer ressursleverandørens
    /// tilgjengelige ressurser innenfor valgt radius.
    /// </summary>
    /// <param name="radiusKm">
    /// Ønsket søkeradius i kilometer.
    /// </param>
    /// <returns>
    /// En liste over behov ressursleverandøren kan hjelpe med.
    /// </returns>
    [HttpGet]
    [ResponseCache(
        Location = ResponseCacheLocation.None,
        NoStore = true)]
    public async Task<IActionResult> MatchingNeeds(
        double radiusKm = ResourceMatchingService.DefaultRadiusKm)
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        radiusKm = NormalizeRadius(radiusKm);

        bool hasAvailableResources =
            await _context.Resources
                .AsNoTracking()
                .AnyAsync(resource =>
                    resource.OwnerUserId == userId
                    && resource.Status == ResourceStatus.Available);

        List<NeedCandidate> needs =
            await _matching.FindNeedsForProviderAsync(
                userId,
                radiusKm,
                maxResults: 25);

        return View(new MatchingNeedsViewModel
        {
            RadiusKm = radiusKm,
            HasAvailableResources = hasAvailableResources,
            Needs = needs
        });
    }

    #endregion

    #region Tilby hjelp

    /// <summary>
    /// Lar ressursleverandøren tilby hjelp med et åpent behov.
    /// Den offentlige aktøren får et varsel og kan deretter
    /// sende en formell forespørsel fra Finn ressurser.
    /// </summary>
    /// <remarks>
    /// Handlingen oppretter ingen tildeling og reserverer
    /// ikke ressursen. Den sender kun et interessetilbud.
    ///
    /// Ressursen må være tilgjengelig, tilhøre den
    /// innloggede brukeren og ha riktig ressurstype.
    /// </remarks>
    /// <param name="needId">
    /// Identifikatoren til behovet.
    /// </param>
    /// <param name="resourceId">
    /// Identifikatoren til ressursen som tilbys.
    /// </param>
    /// <returns>
    /// Omdirigering til behovene brukeren kan hjelpe med.
    /// </returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OfferHelp(
        int needId,
        int resourceId)
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        if (needId <= 0 || resourceId <= 0)
        {
            TempData["RequestError"] =
                "Kunne ikke melde interesse for dette behovet.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        // Kun ressurser som tilhører den innloggede
        // ressursleverandøren kan tilbys.
        Resource? resource =
            await _context.Resources
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.Id == resourceId
                    && r.OwnerUserId == userId);

        Need? need =
            await _context.Needs
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == needId);

        if (resource is null || need is null)
        {
            TempData["RequestError"] =
                "Kunne ikke melde interesse for dette behovet.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        // Ressursen må være tilgjengelig
        // og passe behovets kategori.
        if (resource.Status != ResourceStatus.Available)
        {
            TempData["RequestError"] =
                "Ressursen er ikke tilgjengelig akkurat nå.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        if (resource.ResourceType != need.ResourceType)
        {
            TempData["RequestError"] =
                "Ressurstypen passer ikke til dette behovet.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        // Bare åpne behov kan motta nye tilbud.
        if (need.Status != NeedStatus.New
            && need.Status != NeedStatus.UnderReview)
        {
            TempData["RequestError"] =
                "Dette behovet er ikke lenger åpent.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        // En ressurs som allerede har en aktiv
        // forespørsel til samme behov, trenger
        // ikke å sende et nytt interessetilbud.
        bool hasActiveRequest =
            await _context.NeedAssignments
                .AsNoTracking()
                .AnyAsync(a =>
                    a.NeedId == needId
                    && a.ResourceId == resourceId
                    && (
                        a.Status == AssignmentStatus.Requested
                        || a.Status == AssignmentStatus.Confirmed
                    ));

        if (hasActiveRequest)
        {
            TempData["RequestError"] =
                "Ressursen har allerede en aktiv forespørsel " +
                "for dette behovet.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        // Kontroller at ressursen ikke allerede har
        // en bekreftet tildeling for et annet uløst behov.
        bool bookedElsewhere =
            await _context.NeedAssignments
                .AsNoTracking()
                .AnyAsync(a =>
                    a.ResourceId == resourceId
                    && a.Status == AssignmentStatus.Confirmed
                    && a.Need!.Status != NeedStatus.Resolved);

        if (bookedElsewhere)
        {
            TempData["RequestError"] =
                "Ressursen er allerede tildelt et annet behov.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        // Nye behov eies normalt gjennom Identity.
        // Eldre behov kan fremdeles bare ha OwnerEmail.
        ApplicationUser? needOwner =
            !string.IsNullOrWhiteSpace(need.OwnerUserId)
                ? await _userManager.FindByIdAsync(need.OwnerUserId)
                : !string.IsNullOrWhiteSpace(need.OwnerEmail)
                    ? await _userManager.FindByEmailAsync(
                        need.OwnerEmail)
                    : null;

        if (needOwner is null)
        {
            TempData["RequestError"] =
                "Kunne ikke varsle den offentlige aktøren " +
                "som registrerte behovet.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        // Dette er kun et tilbud om hjelp.
        // Den offentlige aktøren må fortsatt
        // sende en formell forespørsel.
        await _notifications.NotifyAsync(
            needOwner.Id,
            $"Tilbud om hjelp: {resource.ResourceName}",
            $"En frivillig tilbyr «{resource.ResourceName}» " +
            $"({EnumDisplay.Name(resource.ResourceType)}) " +
            $"til behovet «{need.Description}». " +
            "Send en forespørsel fra Finn ressurser " +
            "for å avtale.",
            $"/Match/Need/{need.Id}");

        TempData["RequestMessage"] =
            "Takk! Den offentlige aktøren har fått beskjed " +
            "og kan sende deg en forespørsel.";

        return RedirectToAction(nameof(MatchingNeeds));
    }

    #endregion

    #region Hjelpemetoder

    /// <summary>
    /// Sikrer at søkeradius er et gyldig tall
    /// innenfor portalens tillatte intervall.
    /// </summary>
    /// <param name="radiusKm">
    /// Ønsket søkeradius i kilometer.
    /// </param>
    /// <returns>
    /// Gyldig søkeradius eller standardradius
    /// dersom den oppgitte verdien ikke er et gyldig tall.
    /// </returns>
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

    #endregion
}
