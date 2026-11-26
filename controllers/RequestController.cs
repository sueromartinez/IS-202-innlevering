/*
    RequestController.cs – forespørsler for frivillige

    Hva filen gjør: Den frivillige sin side av matchingen: svare ja eller nei på forespørsler,
    og finne åpne behov i nærheten av egne ressurser.

    På nettsiden: Sidene «Forespørsler» (med knappene Bekreft/Avslå) og «Behov jeg kan hjelpe
    med» (med knappen «Tilby hjelp»).

    Oppgavekrav: 10, 18, 20
*/

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

/*
    Håndterer forespørsler og matchende behov for
    ressursleverandører i Beredskapsportalen.

    Ressursleverandører kan se forespørsler knyttet til
    egne ressurser, svare på forespørsler og tilby hjelp
    til offentlige aktørers åpne behov.

    Tilgang krever innlogging med ResourceProvider-rollen.
    Eierskap kontrolleres i tillegg for hver handling.
*/
[Authorize(Roles = IdentitySeeder.ResourceProviderRole)]
public class RequestController : Controller
{
    #region Felt og konstruktør

    private const double MinRadiusKm = 1;

    private const double MaxRadiusKm = 500;

    private readonly ApplicationDbContext _context;

    private readonly AssignmentService _assignments;

    private readonly ResourceMatchingService _matching;

    private readonly NotificationService _notifications;

    private readonly UserManager<ApplicationUser> _userManager;

    /*
        Oppretter kontrolleren med nødvendige tjenester.

        context:
        Databasekontekst for behov, ressurser og forespørsler.

        assignments:
        Tjeneste for håndtering av forespørsler og tildelinger.

        matching:
        Tjeneste for geografisk matching av behov og ressurser.

        notifications:
        Tjeneste for varsling av brukere.

        userManager:
        Identity-tjeneste for innloggede brukere.
    */
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

    /*
        GET /Request
        Nettleseren skal ikke lagre siden, så «Tilbake»-knappen ikke viser en gammel status.

        Viser forespørsler knyttet til ressursene
        som tilhører den innloggede ressursleverandøren.

        Kansellerte forespørsler utelates fra listen.
        De beholdes i databasen av historikkhensyn.

        Kontaktinformasjon til den offentlige aktøren
        vises bare for bekreftede forespørsler.

        Returnerer:
        En oversikt over ressursleverandørens forespørsler.
    */
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Index()
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        /*
            Hent kun forespørsler for ressurser som
            tilhører den innloggede brukeren.

            Kansellerte forespørsler skal ikke vises
            som aktive eller ubesvarte forespørsler.
        */
        List<NeedAssignment> assignments =
            await _context.NeedAssignments
                .AsNoTracking()
                // Henter behovet og ressursen i samme databasespørring.
                .Include(a => a.Need)
                .Include(a => a.Resource)
                .Where(a =>
                    // Bare forespørsler til mine egne ressurser.
                    a.Resource!.OwnerUserId == userId
                    && a.Status != AssignmentStatus.Cancelled)
                .OrderBy(a => a.Status == AssignmentStatus.Requested ? 0 : 1)
                .ThenByDescending(a => a.CreatedAt)
                .Take(100)
                .ToListAsync();

        /*
            Personvern: kontaktinformasjonen til den som spør, hentes bare for forespørsler som
            er bekreftet.
            På nettsiden: navn og telefon til den offentlige aktøren vises først etter at man
            har trykket «Bekreft».
        */
        List<string> requesterIds = assignments
            .Where(a => a.Status == AssignmentStatus.Confirmed)
            .Select(a => a.RequestedByUserId)
            .Distinct()
            .ToList();

        Dictionary<string, string> requesterContacts =
            await _context.Users
                .AsNoTracking()
                .Where(user => requesterIds.Contains(user.Id))
                .ToDictionaryAsync(user => user.Id, user => $"{user.Name} ({user.Email})");

        /*
            Bygg eksisterende ViewModel slik at
            Razor-visningen fortsatt fungerer.
        */
        List<VolunteerRequestViewModel> model =
            assignments
                .Select(a =>
                {
                    Need need = a.Need!;
                    Resource resource = a.Resource!;

                    Coordinate needPosition = need.Position;
                    Coordinate resourcePosition = resource.Position;

                    double distanceKm =
                        /*
                            Avstand i luftlinje mellom behovet og ressursen. På nettsiden: "12
                            km unna".
                        */
                        GeoDistance.HaversineKm(
                            needPosition.Latitude,
                            needPosition.Longitude,
                            resourcePosition.Latitude,
                            resourcePosition.Longitude);

                    string? requesterContact = null;

                    if (a.Status == AssignmentStatus.Confirmed)
                    {
                        /*
                            Foretrekk behovets registrerte
                            kontaktperson når informasjon finnes.
                        */
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
                                requesterContacts.GetValueOrDefault(a.RequestedByUserId);
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

    /*
        POST /Request/Respond – knappene «Bekreft» og «Avslå». Ved bekreftelse blir ressursen
        opptatt, og den offentlige aktøren får et varsel.

        Lar ressursleverandøren bekrefte eller avslå
        en forespørsel om en av sine egne ressurser.

        id:
        Identifikatoren til forespørselen.

        accept:
        True for å bekrefte, false for å avslå.

        Returnerer:
        Omdirigering til oversikten over forespørsler.
    */
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Respond(int id, bool accept)
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        /*
            AssignmentService kontrollerer også at
            brukeren eier ressursen før status endres.
        */
        ServiceResult result = await _assignments.RespondAsync(id, userId, accept);

        if (result.Success)
        {
            await this.AuditAsync(
                AuditActions.RequestAnswered,
                $"Forespørsel #{id} ble " +
                (accept ? "bekreftet" : "avslått"));
        }

        TempData[
            result.Success ? "RequestSuccess" : "RequestError"
        ] = result.Message;

        return RedirectToAction(nameof(Index));
    }

    #endregion

    #region Behov jeg kan hjelpe med

    /*
        GET /Request/MatchingNeeds
        På nettsiden: «Behov jeg kan hjelpe med», åpne behov av samme type som mine ressurser,
        nærmest først.

        Viser åpne behov som passer ressursleverandørens
        tilgjengelige ressurser innenfor valgt radius.

        radiusKm:
        Ønsket søkeradius i kilometer.

        Returnerer:
        En liste over behov ressursleverandøren kan hjelpe med.
    */
    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
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
            await _matching.FindNeedsForProviderAsync(userId, radiusKm, maxResults: 25);

        return View(new MatchingNeedsViewModel
        {
            RadiusKm = radiusKm,
            HasAvailableResources = hasAvailableResources,
            Needs = needs
        });
    }

    /*
        POST /Request/OfferHelp – knappen «Tilby hjelp». Dette er bare et tilbud. Den offentlige
        aktøren får et varsel og bestemmer selv om hen vil sende en forespørsel. Mange sjekker
        først: eier jeg ressursen, er den ledig, passer typen, og er behovet fortsatt åpent?

        Lar ressursleverandøren tilby hjelp med et åpent behov.
        Den offentlige aktøren får et varsel og kan deretter
        sende en formell forespørsel fra Finn ressurser.

        Handlingen oppretter ingen tildeling og reserverer
        ikke ressursen. Den sender kun et interessetilbud.

        Ressursen må være tilgjengelig, tilhøre den
        innloggede brukeren og ha riktig ressurstype.

        needId:
        Identifikatoren til behovet.

        resourceId:
        Identifikatoren til ressursen som tilbys.

        Returnerer:
        Omdirigering til behovene brukeren kan hjelpe med.
    */
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OfferHelp(int needId, int resourceId)
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        if (needId <= 0 || resourceId <= 0)
        {
            TempData["RequestError"] = "Kunne ikke melde interesse for dette behovet.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        /*
            Kun ressurser som tilhører den innloggede
            ressursleverandøren kan tilbys.
        */
        Resource? resource =
            await _context.Resources
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == resourceId && r.OwnerUserId == userId);

        Need? need =
            await _context.Needs
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == needId);

        if (resource is null || need is null)
        {
            TempData["RequestError"] = "Kunne ikke melde interesse for dette behovet.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        /*
            Ressursen må være tilgjengelig
            og passe behovets kategori.
        */
        if (resource.Status != ResourceStatus.Available)
        {
            TempData["RequestError"] = "Ressursen er ikke tilgjengelig akkurat nå.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        if (resource.ResourceType != need.ResourceType)
        {
            TempData["RequestError"] = "Ressurstypen passer ikke til dette behovet.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        // Bare åpne behov kan motta nye tilbud.
        if (need.Status != NeedStatus.New && need.Status != NeedStatus.UnderReview)
        {
            TempData["RequestError"] = "Dette behovet er ikke lenger åpent.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        /*
            En ressurs som allerede har en aktiv
            forespørsel til samme behov, trenger
            ikke å sende et nytt interessetilbud.
        */
        bool hasActiveRequest =
            await _context.NeedAssignments
                .AsNoTracking()
                .AnyAsync(a =>
                    a.NeedId == needId
                    && a.ResourceId == resourceId
                    && (
                        a.Status == AssignmentStatus.Requested
                        || a.Status == AssignmentStatus.Confirmed));

        if (hasActiveRequest)
        {
            TempData["RequestError"] =
                "Ressursen har allerede en aktiv forespørsel " +
                "for dette behovet.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        /*
            Kontroller at ressursen ikke allerede har
            en bekreftet tildeling for et annet uløst behov.
        */
        bool bookedElsewhere =
            await _context.NeedAssignments
                .AsNoTracking()
                .AnyAsync(a =>
                    a.ResourceId == resourceId
                    && a.Status == AssignmentStatus.Confirmed
                    && a.Need!.Status != NeedStatus.Resolved);

        if (bookedElsewhere)
        {
            TempData["RequestError"] = "Ressursen er allerede tildelt et annet behov.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        /*
            Nye behov eies normalt gjennom Identity.
            Eldre behov kan fremdeles bare ha OwnerEmail.
        */
        ApplicationUser? needOwner =
            !string.IsNullOrWhiteSpace(need.OwnerUserId)
                ? await _userManager.FindByIdAsync(need.OwnerUserId)
                : !string.IsNullOrWhiteSpace(need.OwnerEmail)
                    ? await _userManager.FindByEmailAsync(need.OwnerEmail)
                    : null;

        if (needOwner is null)
        {
            TempData["RequestError"] =
                "Kunne ikke varsle den offentlige aktøren " +
                "som registrerte behovet.";

            return RedirectToAction(nameof(MatchingNeeds));
        }

        /*
            Varselet har en lenke rett til «Finn ressurser» for behovet.
        */
        await _notifications.NotifyAsync(
            needOwner.Id,
            $"Tilbud om hjelp: {resource.ResourceName}",
            $"En frivillig tilbyr «{resource.ResourceName}» " +
            $"({EnumDisplay.Name(resource.ResourceType)}) " +
            $"til behovet «{need.Description}». " +
            "Send en forespørsel fra Finn ressurser " +
            "for å avtale.",
            $"/Match/Need/{need.Id}");

        TempData["RequestSuccess"] =
            "Takk! Den offentlige aktøren har fått beskjed " +
            "og kan sende deg en forespørsel.";

        return RedirectToAction(nameof(MatchingNeeds));
    }

    #endregion

    #region Hjelpemetoder

    /*
        Sikrer at søkeradius er et gyldig tall
        innenfor portalens tillatte intervall.

        radiusKm:
        Ønsket søkeradius i kilometer.

        Returnerer:
        Gyldig søkeradius eller standardradius
        dersom den oppgitte verdien ikke er et gyldig tall.
    */
    private static double NormalizeRadius(double radiusKm)
    {
        if (!double.IsFinite(radiusKm))
        {
            return ResourceMatchingService.DefaultRadiusKm;
        }

        return Math.Clamp(radiusKm, MinRadiusKm, MaxRadiusKm);
    }

    #endregion
}
