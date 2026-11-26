/*
    MatchController.cs – koordinering for offentlig aktør

    Hva filen gjør: Her kobles behov til ressurser: finne ressurser i nærheten, sende
    forespørsler, avbryte dem og markere behov som løst. Controlleren gjør selv bare
    tilgangskontroll og velger svar. Selve søket ligger i ResourceMatchingService, og reglene
    for forespørsler ligger i AssignmentService.

    På nettsiden: Sidene «Koordinering» (tavle med kolonner), «Finn ressurser» for ett behov
    (liste + kart) og «Tildelinger».

    Oppgavekrav: 10, 18, 19, 20, 22
*/

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

/*
    Håndterer koordinering, ressursmatching og tildelinger
    for offentlige aktører og administratorer.

    Innlogging og roller kontrolleres med ASP.NET Core Identity.

    Offentlige aktører kan se koordineringsoversikten,
    men kan bare endre forespørsler på behov de selv eier.
    Administratorer kan administrere alle behov.

    Permanente eierskapskontroller bruker OwnerUserId.
    OwnerEmail støttes for eldre behov uten Identity-eier.
*/
[Authorize(Roles = IdentitySeeder.PublicActorRole + "," + IdentitySeeder.AdminRole)]
public class MatchController : Controller
{
    #region Felt og konstruktør

    // Søkeradius må være mellom 1 og 500 km, så ingen kan sende inn rare verdier i adressen.
    private const double MinRadiusKm = 1;

    private const double MaxRadiusKm = 500;

    private readonly ApplicationDbContext _context;

    private readonly ResourceMatchingService _matching;

    private readonly AssignmentService _assignments;

    private readonly UserManager<ApplicationUser> _userManager;

    /*
        Oppretter kontrolleren med database-, matching-,
        tildelings- og Identity-tjenester.

        context: Applikasjonens databasekontekst.
        matching: Tjeneste for ressursmatching.
        assignments: Tjeneste for forespørsler og tildelinger.
        userManager: Identity-tjeneste for brukere.
    */
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

    #region Koordinering

    /*
        GET /Match
        På nettsiden: «Koordinering», en tavle der hvert behov står i en kolonne ut fra om det
        trenger ressurser, venter på svar eller er bekreftet. Antall forespørsler telles i én
        omgang i stedet for én databasespørring per behov.
    */
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

        /*
            Bare Requested og Confirmed skal telle
            som aktive forespørsler på Kanban-tavlen.
        */
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
            .ToDictionary(group => group.Key, group => group.ToList());

        Dictionary<int, int> pendingByNeed = new Dictionary<int, int>();
        Dictionary<int, int> confirmedByNeed = new Dictionary<int, int>();
        Dictionary<int, int> totalByNeed = new Dictionary<int, int>();

        foreach (Need need in allNeeds)
        {
            int pending = 0;
            int confirmed = 0;

            if (groupedAssignments.TryGetValue(need.Id, out var relatedAssignments))
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

        // Tallene sendes til viewet og vises på kortene på tavla.
        ViewBag.PendingByNeed = pendingByNeed;
        ViewBag.ConfirmedByNeed = confirmedByNeed;
        ViewBag.TotalByNeed = totalByNeed;
        ViewBag.ResolvedNeeds = resolvedNeeds;

        ViewBag.TotalActiveNeeds = activeNeeds.Count;
        ViewBag.TotalPending = activeNeeds.Sum(need => pendingByNeed[need.Id]);

        ViewBag.TotalConfirmed = activeNeeds.Sum(need => confirmedByNeed[need.Id]);

        ViewBag.TotalResolvedNeeds = resolvedNeeds.Count;

        return View(activeNeeds);
    }

    #endregion

    #region Tildelinger

    /*
        GET /Match/Assignments
        På nettsiden: «Tildelinger», alle forespørsler gruppert per behov, med kontaktinfo til
        ressursen.

        Viser registrerte forespørsler og tildelinger,
        gruppert etter behov.

        Kansellerte forespørsler skjules fra oversikten,
        men beholdes i MariaDB for historikk og sporbarhet.

        Returnerer: En liste med MatchNeedViewModel.
    */
    [HttpGet]
    public async Task<IActionResult> Assignments()
    {
        var assignmentRows =
            await _context.NeedAssignments
                .AsNoTracking()
                .Where(assignment => assignment.Status != AssignmentStatus.Cancelled)
                .OrderByDescending(assignment => assignment.CreatedAt)
                .Select(assignment => new
                {
                    assignment.NeedId,

                    Row = new AssignmentRowViewModel
                    {
                        Id = assignment.Id,

                        ResourceName = assignment.Resource!.ResourceName,

                        ResourceType = assignment.Resource.ResourceType,

                        Status = assignment.Status,
                        Message = assignment.Message,

                        SuggestedByAssistant = assignment.SuggestedByAssistant,

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

        List<MatchNeedViewModel> model = new List<MatchNeedViewModel>();

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
                assignmentsByNeed.TryGetValue(need.Id, out List<AssignmentRowViewModel>? found)
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

    #region Finn ressurser

    /*
        GET /Match/Need/5?radiusKm=50
        På nettsiden: «Finn ressurser». Behovet vises øverst. Under står passende ressurser
        sortert etter avstand, et kart med behovet og ressursene, og forespørslene som allerede
        er sendt.

        Viser et behov og finner tilgjengelige ressurser
        som passer behovets kategori og geografiske plassering.

        id: Behovets identifikator.
        radiusKm: Søkeradius i kilometer.
        Returnerer: Detaljsiden for behovet med ressurskandidater.
    */
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
                /*
                    Søket: samme type, tilgjengelig, innenfor radius, nærmest først, og ikke
                    ressurser som allerede er spurt. Sami bruker den samme metoden, så Sami og
                    nettsiden gir samme svar.
                */
                : await _matching.FindNearbyResourcesAsync(
                    needPosition.Latitude,
                    needPosition.Longitude,
                    radiusKm,
                    new[] { need.ResourceType },
                    maxResults: 15,
                    excludeRequestedForNeedId: need.Id);

        /*
            Kartposisjonene ligger utenfor ResourceCandidate
            fordi denne returtypen også brukes av Sami.
        */
        List<int> candidateIds = candidates
            .Select(candidate => candidate.ResourceId)
            .ToList();

        // Plasseringene sendes med til viewet, slik at de kan tegnes som markører på kartet.
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

        /*
            Kansellerte forespørsler skal ikke vises,
            men historikken beholdes i databasen.
        */
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

                    ResourceName = assignment.Resource!.ResourceName,

                    ResourceType = assignment.Resource.ResourceType,

                    Status = assignment.Status,
                    Message = assignment.Message,

                    SuggestedByAssistant = assignment.SuggestedByAssistant,

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

    #region Send og avbryt forespørsel

    /*
        POST /Match/SendRequest
        På nettsiden: knappen «Send forespørsel» ved en ressurs, og samme knapp i forslagene fra
        Sami.
        Den frivillige får et varsel.

        Sender en forespørsel om en ressurs til et behov
        som den innloggede brukeren har lov til å administrere.

        needId: Behovets identifikator.
        resourceId: Ressursens identifikator.
        message: Valgfri melding til ressurseieren.
        suggestedByAssistant:
        Angir om ressursen er foreslått av Sami.

        Returnerer: JSON eller omdirigering til behovets detaljside.
    */
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

        /*
            En offentlig aktør skal bare administrere
            sine egne behov. Admin kan administrere alle.
        */
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

        /*
            Samme adresse brukes av vanlige skjemaer (som får en ny side) og av JavaScript/Sami
            (som får JSON tilbake).
        */
        if (WantsJson())
        {
            return Json(new
            {
                success = result.Success,
                message = result.Message
            });
        }

        TempData[
            result.Success ? "MatchSuccess" : "MatchError"
        ] = result.Message;

        return RedirectToAction(
            nameof(Need),
            new { id = needId });
    }

    /*
        POST /Match/Cancel – knappen «Avbryt» ved en sendt forespørsel. Vi sjekker at
        forespørselen faktisk hører til behovet, fordi ID-er fra skjemaet kan endres i
        nettleseren.

        Avbryter en forespørsel dersom brukeren eier
        behovet eller har administratorrollen.

        Identifikatorene kontrolleres mot databasen før
        AssignmentService.CancelAsync kalles.

        Kansellering endrer forespørselens status, men
        sletter ikke forespørselen fra databasen.

        assignmentId: Forespørselens identifikator.
        needId: Behovet forespørselen tilhører.
        Returnerer: Omdirigering til behovets detaljside.
    */
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int assignmentId, int needId)
    {
        string? userId = _userManager.GetUserId(User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        /*
            Kontroller at forespørselen faktisk tilhører
            behovet som ble sendt inn fra skjemaet.
        */
        NeedAssignment? assignment =
            await _context.NeedAssignments
                .AsNoTracking()
                .Include(item => item.Need)
                .FirstOrDefaultAsync(item => item.Id == assignmentId && item.NeedId == needId);

        if (assignment?.Need is null)
        {
            return NotFound();
        }

        /*
            Stopp handlingen før CancelAsync dersom
            brukeren ikke har eierskap til behovet.
        */
        if (!await CanManageNeedAsync(assignment.Need, userId))
        {
            return Forbid();
        }

        ServiceResult result = await _assignments.CancelAsync(assignment.Id);

        if (result.Success)
        {
            await this.AuditAsync(
                AuditActions.RequestCancelled,
                $"Forespørsel #{assignment.Id} " +
                $"på behov #{assignment.NeedId} ble avbrutt");
        }

        TempData[
            result.Success ? "MatchSuccess" : "MatchError"
        ] = result.Message;

        /*
            Bruk verifisert NeedId fra databasen,
            ikke en ubekreftet ID fra skjemaet.
        */
        return RedirectToAction(
            nameof(Need),
            new { id = assignment.NeedId });
    }

    #endregion

    #region Marker behov som løst

    /*
        POST /Match/Resolve – knappen «Marker som løst». Ressursene blir ledige igjen.

        Marker et behov som løst og frigjør ressursene
        som ikke lenger er i bruk.

        needId: Behovets identifikator.
        Returnerer: Omdirigering til behovets detaljside.
    */
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

        /*
            Bare behovets eier eller administrator
            skal kunne markere behovet som løst.
        */
        if (!await CanManageNeedAsync(need, userId))
        {
            return Forbid();
        }

        ServiceResult result = await _assignments.ResolveNeedAsync(need.Id);

        if (result.Success)
        {
            await this.AuditAsync(AuditActions.NeedResolved, $"Behov #{need.Id} markert som løst");
        }

        TempData[
            result.Success ? "MatchSuccess" : "MatchError"
        ] = result.Message;

        return RedirectToAction(
            nameof(Need),
            new { id = need.Id });
    }

    #endregion

    #region Hjelpemetoder

    /*
        Regelen for hvem som kan endre et behov: admin kan alt, ellers må du eie behovet.

        Kontrollerer om den innloggede brukeren har rett
        til å administrere et bestemt behov.

        Administratorer kan administrere alle behov.

        For andre brukere brukes OwnerUserId når den finnes.
        OwnerEmail brukes bare for eldre behov uten
        permanent Identity-eier.

        need: Behovet som skal administreres.
        userId: Innlogget brukers Identity-ID.
        Returnerer:
        True dersom brukeren er administrator eller eier behovet.
    */
    private async Task<bool> CanManageNeedAsync(Need need, string userId)
    {
        if (User.IsInRole(IdentitySeeder.AdminRole))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(need.OwnerUserId))
        {
            return string.Equals(need.OwnerUserId, userId, StringComparison.Ordinal);
        }

        /*
            Støtt eldre behov som bare har OwnerEmail.
            Ikke bruk e-post som reserve dersom
            OwnerUserId allerede er registrert.
        */
        if (string.IsNullOrWhiteSpace(need.OwnerEmail))
        {
            return false;
        }

        ApplicationUser? currentUser = await _userManager.GetUserAsync(User);

        if (currentUser is null || string.IsNullOrWhiteSpace(currentUser.Email))
        {
            return false;
        }

        return string.Equals(
            currentUser.Email,
            need.OwnerEmail,
            StringComparison.OrdinalIgnoreCase);
    }

    /*
        Begrens søkeradius til portalens tillatte intervall.

        radiusKm: Ønsket radius i kilometer.
        Returnerer: En gyldig radius.
    */
    private static double NormalizeRadius(double radiusKm)
    {
        if (!double.IsFinite(radiusKm))
        {
            return ResourceMatchingService.DefaultRadiusKm;
        }

        return Math.Clamp(radiusKm, MinRadiusKm, MaxRadiusKm);
    }

    /*
        Kontrollerer om forespørselen ønsker JSON-respons.

        Returnerer: True dersom Accept-headeren inneholder application/json.
    */
    private bool WantsJson()
    {
        return HttpContext.Request.Headers.Accept
            .ToString()
            .Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
