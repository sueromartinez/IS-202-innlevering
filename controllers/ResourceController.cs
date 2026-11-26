/*
    ResourceController.cs – ressurser

    Hva filen gjør: Tar imot alt som har med ressurser å gjøre: registrere, vise, endre, slette
    og sette tilgjengelighet. Hver side har som regel to metoder: en GET som viser skjemaet, og
    en POST som tar imot det brukeren sendte inn, sjekker det, lagrer det og sender brukeren
    videre.
    Etter en vellykket POST sender vi brukeren til en ny side (RedirectToAction). Dette heter
    Post-Redirect-Get og gjør at skjemaet ikke sendes på nytt hvis man trykker F5.

    På nettsiden: Sidene «Registrer ressurs», «Endre ressurs» og «Ressurser»/«Mine ressurser»
    (liste + kart), og knappene for å endre status eller slette en ressurs.

    Oppgavekrav: 9, 11, 13, 18, 20, 21, 22
*/

using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/*
    Bare innloggede brukere med en av de tre rollene slipper inn. Sjekken skjer før koden i
    metoden kjøres. Noen metoder strammer inn ytterligere, for eksempel at bare frivillige kan
    registrere.
*/
[Authorize(
    Roles =
        IdentitySeeder.ResourceProviderRole
        + ","
        + IdentitySeeder.PublicActorRole
        + ","
        + IdentitySeeder.AdminRole)]
public class ResourceController : Controller
{
    #region Felt og konstruktør

    private readonly ApplicationDbContext _context;

    private readonly UserManager<ApplicationUser> _userManager;

    /*
        ASP.NET gir oss databasen og brukerhåndteringen automatisk (registrert i Program.cs). Vi
        lager dem aldri selv med "new".
    */
    public ResourceController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    #endregion

    #region Registrer ressurs

    /*
        GET /Resource/RegisterResource
        På nettsiden: viser det tomme skjemaet «Registrer ressurs» med kartet ved siden av.
    */
    [HttpGet]
    [Authorize(Roles = IdentitySeeder.ResourceProviderRole)]
    public IActionResult RegisterResource()
    {
        return View(new RegisterResourceViewModel());
    }

    /*
        POST /Resource/RegisterResource
        På nettsiden: kjøres når den frivillige trykker «Registrer ressurs» nederst i skjemaet.
        Steg for steg:
        1) [ValidateAntiForgeryToken] sjekker den skjulte sikkerhetskoden i skjemaet. Mangler
           den, avvises innsendingen. Det hindrer at en annen nettside sender skjemaer i ditt
           navn (CSRF).
        2) ASP.NET fyller RegisterResourceViewModel med feltene fra skjemaet. Vi bruker en egen
           ViewModel og ikke Resource direkte, så brukeren ikke kan sende inn felt som hvem som
           eier ressursen.
        3) Feltene sjekkes, ressursen lagres, og brukeren sendes til dashbordet.
    */
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = IdentitySeeder.ResourceProviderRole)]
    public async Task<IActionResult> RegisterResource(RegisterResourceViewModel model)
    {
        // Krever en gyldig periode selv om klientvalidering er slått av.
        if (!model.AvailabilityMonths.HasValue
            || !ResourceAvailability.Periods.ContainsKey(model.AvailabilityMonths.Value))
        {
            ModelState.AddModelError(nameof(model.AvailabilityMonths), "Velg en gyldig tilgjengelighetsperiode.");
        }

        /*
            ModelState sier om skjemaet er gyldig, ut fra reglene i ViewModel-en.
            På nettsiden: er noe feil, vises samme skjema igjen med røde feilmeldinger, og det
            brukeren allerede har skrevet står der fortsatt.
            Vi sjekker på serveren selv om nettleseren også sjekker, fordi kontrollen i
            nettleseren kan omgås.
        */
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!model.ResourceType.HasValue)
        {
            ModelState.AddModelError(nameof(model.ResourceType), "Velg type ressurs.");

            return View(model);
        }

        // Finner hvem som er innlogget. Er det noe galt, sendes brukeren til innlogging.
        ApplicationUser? user = await _userManager.GetUserAsync(User);

        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return Challenge();
        }

        Resource resource =
            /*
                Flytter verdiene fra skjemaet over til en ny Resource som skal lagres. Tomme
                valgfrie felt lagres som tomme (null), og mellomrom før og etter tekst fjernes.
            */
            new Resource
            {
                // --- Ressursinformasjon ---

                ResourceType = model.ResourceType.Value,

                ResourceName = model.ResourceName.Trim(),

                Description = model.Description.Trim(),

                Quantity = model.Quantity,

                Capacity = NormalizeOptionalText(model.Capacity),

                // Lagrer valgt periode og sluttdato i eksisterende felt.
                Availability = ResourceAvailability.CreatePeriod(model.AvailabilityMonths!.Value, DateTime.UtcNow),

                // --- Plassering ---

                Address = NormalizeOptionalText(model.CombinedAddress()),

                Latitude = model.Latitude,

                Longitude = model.Longitude,

                // --- Kontakt ---

                ContactEmail =
                    string.IsNullOrWhiteSpace(model.ContactEmail)
                        ? user.Email
                        : model.ContactEmail.Trim(),

                ContactPhoneCountryCode = NormalizeOptionalText(model.ContactPhoneCountryCode),

                ContactPhoneNumber = NormalizeOptionalText(model.ContactPhoneNumber),

                /*
                    OWNERSHIP

                    OwnerUserId er den permanente
                    koblingen mellom ressurs og bruker.

                    OwnerEmail beholdes foreløpig
                    for bakoverkompatibilitet.
                */

                /*
                    Eieren settes fra den innloggede brukeren, aldri fra skjemaet. Ellers kunne
                    noen registrert ressurser i andres navn.
                */
                OwnerUserId = user.Id,

                OwnerEmail = user.Email,

                // --- Metadata ---

                CreatedAt =
                    DateTime.UtcNow
            };

        /*
            Add() forbereder lagringen, og SaveChangesAsync() skriver ressursen til databasen.
            "await" gjør at serveren kan hjelpe andre brukere mens den venter på databasen.
        */
        _context.Resources.Add(resource);

        await _context
            .SaveChangesAsync();

        // Skriver i aktivitetsloggen at ressursen ble opprettet (se Services/AuditLog.cs).
        await this.AuditAsync(
            AuditActions.ResourceCreated,
            $"Registrerte ressursen «{resource.ResourceName}» (#{resource.Id}).");

        /*
            TempData huskes til neste side. På nettsiden: den grønne meldingen «Ressursen ble
            registrert» og konfetti-animasjonen (_Feiring.cshtml) etter at man er sendt videre.
        */
        TempData["ResourceSuccess"] = "Ressursen ble registrert.";

        // Fortell oversikten at den skal vise feiring (gif, konfetti, sang)
        TempData["ResourceCelebrate"] = true;

        return RedirectToAction("Dashboard", "Home");
    }

    #endregion

    #region Ressursoversikt og status

    /*
        GET /Resource/ResourceOverview
        På nettsiden: «Ressurser»/«Mine ressurser» med kort for hver ressurs og et kart med
        markører. Frivillige ser bare sine egne ressurser. Offentlige aktører og admin ser alle.
    */
    [HttpGet]
    public async Task<IActionResult> ResourceOverview()
    {
        ApplicationUser? user = await _userManager.GetUserAsync(User);

        if (user is null || string.IsNullOrWhiteSpace(user.Email))
        {
            return Challenge();
        }

        bool isPublicActor = User.IsInRole(IdentitySeeder.PublicActorRole);

        bool isAdmin = User.IsInRole(IdentitySeeder.AdminRole);

        bool isResourceProvider = User.IsInRole(IdentitySeeder.ResourceProviderRole);

        /*
            LINK LEGACY RESOURCES

            Eldre ressurser kan være opprettet før
            OwnerUserId ble innført.

            Dersom en slik ressurs har samme
            OwnerEmail som den innloggede brukeren,
            kobles den nå permanent til brukerens
            Identity-ID.

            Dette gjør at ressursen fortsatt tilhører
            brukeren selv om e-postadressen endres.
        */

        if (isResourceProvider && !isPublicActor && !isAdmin)
        {
            /*
                Rydder i gamle data: eldre ressurser ble koblet til eieren via e-post. Nå kobles
                de til bruker-ID, som ikke endres selv om e-posten endres.
            */
            await LinkLegacyResourcesToIdentityUserAsync(user);
        }

        IQueryable<Resource> query =
            _context.Resources
                /*
                    AsNoTracking: vi skal bare vise data, ikke endre dem. Det gjør spørringen
                    raskere.
                */
                .AsNoTracking();

        /*
            Ressursleverandører ser bare
            ressurser de selv eier.
        */
        if (isResourceProvider && !isPublicActor && !isAdmin)
        {
            query =
                /*
                    For frivillige legges filteret "bare mine ressurser" på selve
                    databasespørringen. Andres ressurser hentes altså aldri, i stedet for å bare
                    skjules i HTML-en.
                */
                query.Where(
                    resource =>
                        resource.OwnerUserId
                            == user.Id

                        ||

                        (resource.OwnerUserId == null  &&  resource.OwnerEmail == user.Email));
        }

        List<ResourceOverviewViewModel> resources =
            await query
                .OrderByDescending(resource => resource.CreatedAt)
                // Henter bare kolonnene siden trenger, rett inn i ViewModel-en.
                .Select(
                    resource =>
                        new ResourceOverviewViewModel
                        {
                            Id = resource.Id,

                            ResourceType = resource.ResourceType,

                            // Sender faktiske navn, perioder og adresser til søk og ressurskort.
                            ResourceName = resource.ResourceName,
                            Quantity = resource.Quantity,
                            Capacity = resource.Capacity,
                            Availability = resource.Availability,
                            Address = resource.Address,

                            Description = resource.Description,

                            Position = new Coordinate(resource.Latitude, resource.Longitude),

                            ContactEmail = resource.ContactEmail,

                            ContactPhoneCountryCode = resource.ContactPhoneCountryCode,

                            ContactPhoneNumber = resource.ContactPhoneNumber,

                            OwnerEmail = resource.OwnerEmail,

                            CreatedAt = resource.CreatedAt,

                            Status =
                                resource.Status
                        })
                .ToListAsync();

        if (isAdmin)
        {
            // Sender rollen til viewet, slik at det kan vise eller skjule knapper.
            ViewBag.UserRole = IdentitySeeder.AdminRole;
        }
        else if (isPublicActor)
        {
            ViewBag.UserRole = IdentitySeeder.PublicActorRole;
        }
        else
        {
            ViewBag.UserRole = IdentitySeeder.ResourceProviderRole;
        }

        return View(resources);
    }

    /*
        POST /Resource/SetStatus
        På nettsiden: knappene «Tilgjengelig» og «Utilgjengelig» på hver ressurs. «Opptatt»
        settes bare av systemet når ressursen er tildelt et behov, så det kan ikke velges her.
    */
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = IdentitySeeder.ResourceProviderRole)]
    public async Task<IActionResult> SetStatus(int id, ResourceStatus status)
    {
        if (status == ResourceStatus.Busy)
        {
            return BadRequest();
        }

        string? userId = _userManager.GetUserId(User);

        Resource? resource =
            await _context.Resources
                /*
                    Finnes ikke ressursen, eller eies den av noen andre, svarer vi "finnes ikke"
                    (404). Da kan man ikke prøve seg frem med andre ID-er i adressen.
                */
                .FirstOrDefaultAsync(item => item.Id == id && item.OwnerUserId == userId);

        if (resource is null)
        {
            return NotFound();
        }

        if (resource.Status == ResourceStatus.Busy)
        {
            TempData["ResourceSuccess"] =
                "Ressursen er opptatt med et behov. Den blir ledig når behovet er løst eller forespørselen avbrutt.";

            return RedirectToAction(nameof(ResourceOverview));
        }

        resource.Status = status;

        await _context
            .SaveChangesAsync();

        await this.AuditAsync(
            AuditActions.ResourceStatusChanged,
            $"Markerte ressursen «{resource.ResourceName}» (#{resource.Id}) som {EnumDisplay.Name(status).ToLowerInvariant()}.");

        TempData["ResourceSuccess"] =
            status == ResourceStatus.Available
                ? "Ressursen er markert som tilgjengelig."
                : "Ressursen er markert som utilgjengelig og vises ikke i søk.";

        return RedirectToAction(nameof(ResourceOverview));
    }

    #endregion

    #region Endre og slette ressurs

    /*
        GET /Resource/EditResource/5
        På nettsiden: «Endre ressurs». Bruker samme side som registreringen, men med feltene
        utfylt. Slik slipper vi to nesten like sider.
    */
    [HttpGet]
    [Authorize(Roles = IdentitySeeder.ResourceProviderRole)]
    public async Task<IActionResult> EditResource(int id)
    {
        Resource? resource = await FindOwnResourceAsync(id);

        if (resource is null)
        {
            return NotFound();
        }

        ViewData["EditResourceId"] = resource.Id;

        return View(nameof(RegisterResource), ToViewModel(resource));
    }

    // POST /Resource/EditResource
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = IdentitySeeder.ResourceProviderRole)]
    public async Task<IActionResult> EditResource(int id, RegisterResourceViewModel model)
    {
        Resource? resource = await FindOwnResourceAsync(id);

        if (resource is null)
        {
            return NotFound();
        }

        if (!model.AvailabilityMonths.HasValue
            || !ResourceAvailability.Periods.ContainsKey(model.AvailabilityMonths.Value))
        {
            ModelState.AddModelError(nameof(model.AvailabilityMonths), "Velg en gyldig tilgjengelighetsperiode.");
        }

        if (!model.ResourceType.HasValue)
        {
            ModelState.AddModelError(nameof(model.ResourceType), "Velg type ressurs.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["EditResourceId"] = resource.Id;

            return View(nameof(RegisterResource), model);
        }

        ApplicationUser? user = await _userManager.GetUserAsync(User);

        resource.ResourceType = model.ResourceType!.Value;
        resource.ResourceName = model.ResourceName.Trim();
        resource.Description = model.Description.Trim();
        resource.Quantity = model.Quantity;
        resource.Capacity = NormalizeOptionalText(model.Capacity);

        // Perioden regnes fra i dag når ressursen lagres på nytt.
        resource.Availability =
            ResourceAvailability.CreatePeriod(model.AvailabilityMonths!.Value, DateTime.UtcNow);

        resource.Address = NormalizeOptionalText(model.CombinedAddress());
        resource.Latitude = model.Latitude;
        resource.Longitude = model.Longitude;

        resource.ContactEmail =
            string.IsNullOrWhiteSpace(model.ContactEmail)
                ? user?.Email ?? resource.ContactEmail
                : model.ContactEmail.Trim();

        resource.ContactPhoneCountryCode = NormalizeOptionalText(model.ContactPhoneCountryCode);
        resource.ContactPhoneNumber = NormalizeOptionalText(model.ContactPhoneNumber);

        await _context.SaveChangesAsync();

        await this.AuditAsync(
            AuditActions.ResourceUpdated,
            $"Endret ressursen «{resource.ResourceName}» (#{resource.Id}).");

        TempData["ResourceSuccess"] = $"Endringene på «{resource.ResourceName}» er lagret.";

        return RedirectToAction(nameof(ResourceOverview));
    }

    /*
        POST /Resource/DeleteResource
        På nettsiden: «Slett»-knappen. En opptatt ressurs kan ikke slettes, og heller ikke en
        ressurs som har forespørsler (historikk). Da skal den heller settes til utilgjengelig.

        Lar en ressursleverandør slette sin egen ressurs.

        Samme regler som når admin sletter: en ressurs som er opptatt,
        eller som har forespørsler/tildelinger knyttet til seg, slettes ikke,
        fordi historikken skal beholdes. Da kan den markeres som utilgjengelig i stedet.
    */
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = IdentitySeeder.ResourceProviderRole)]
    public async Task<IActionResult> DeleteResource(int id)
    {
        Resource? resource = await FindOwnResourceAsync(id);

        if (resource is null)
        {
            return NotFound();
        }

        if (resource.Status == ResourceStatus.Busy)
        {
            TempData["ResourceError"] =
                $"«{resource.ResourceName}» er opptatt med et behov og kan ikke slettes nå.";

            return RedirectToAction(nameof(ResourceOverview));
        }

        bool hasHistory =
            await _context.NeedAssignments
                .AnyAsync(assignment => assignment.ResourceId == resource.Id);

        if (hasHistory)
        {
            TempData["ResourceError"] =
                $"«{resource.ResourceName}» har forespørsler knyttet til seg, og historikken må beholdes. " +
                "Marker den som utilgjengelig i stedet.";

            return RedirectToAction(nameof(ResourceOverview));
        }

        string name = resource.ResourceName;
        int resourceId = resource.Id;

        _context.Resources.Remove(resource);

        await _context.SaveChangesAsync();

        await this.AuditAsync(
            AuditActions.ResourceDeleted,
            $"Slettet ressursen «{name}» (#{resourceId}).");

        TempData["ResourceSuccess"] = $"«{name}» er slettet.";

        return RedirectToAction(nameof(ResourceOverview));
    }

    #endregion

    #region Hjelpemetoder

    /*
        Felles hjelpemetode: "finn ressursen, men bare hvis den er min". Brukes av endre og
        slette, så eierskapssjekken bare står ett sted.
    */
    private async Task<Resource?> FindOwnResourceAsync(int id)
    {
        ApplicationUser? user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return null;
        }

        string? email = user.Email;

        return await _context.Resources
            .FirstOrDefaultAsync(resource =>
                resource.Id == id
                && (resource.OwnerUserId == user.Id
                    || (resource.OwnerUserId == null && email != null && resource.OwnerEmail == email)));
    }

    /*
        Motsatt vei: gjør en lagret ressurs om til skjemaverdier når den skal endres. Adressen
        er lagret som én tekst og deles opp igjen i gate, postnummer og kommune.
    */
    private static RegisterResourceViewModel ToViewModel(Resource resource)
    {
        string? street = resource.Address;
        string? postal = null;
        string? municipality = null;

        string[] mainParts = resource.Address?.Split(" – ", 2) ?? Array.Empty<string>();

        /*
            Bare adresser i formatet fra skjemaet deles opp. Alt annet
            (f.eks. eldre fritekst) beholdes uendret i adressefeltet.
        */
        if (mainParts.Length == 2 && mainParts[1].EndsWith(" kommune", StringComparison.Ordinal))
        {
            municipality = mainParts[1][..^" kommune".Length].Trim();

            int comma = mainParts[0].IndexOf(", ", StringComparison.Ordinal);

            if (comma > 0)
            {
                street = mainParts[0][..comma].Trim();
                postal = mainParts[0][(comma + 2)..].Trim();
            }
            else
            {
                street = mainParts[0].Trim();
            }
        }

        // Finn perioden som ble valgt sist, f.eks. "6 måneder; til 2027-05-01".
        int? months =
            ResourceAvailability.Periods
                .Where(period => resource.Availability?.StartsWith(period.Value + ";") == true)
                .Select(period => (int?)period.Key)
                .FirstOrDefault();

        return new RegisterResourceViewModel
        {
            ResourceType = resource.ResourceType,
            ResourceName = resource.ResourceName,
            Description = resource.Description,
            Quantity = resource.Quantity,
            Capacity = resource.Capacity,
            AvailabilityMonths = months,
            Address = resource.Address,
            StreetAddress = street,
            PostalLocation = postal,
            Municipality = municipality,
            Latitude = resource.Latitude,
            Longitude = resource.Longitude,
            ContactEmail = resource.ContactEmail,
            ContactPhoneCountryCode = resource.ContactPhoneCountryCode ?? "+47",
            ContactPhoneNumber = resource.ContactPhoneNumber
        };
    }

    /*
        Kobler eldre ressurser som bare har
        OwnerEmail til brukerens permanente
        ASP.NET Core Identity-ID.

        Dette er overgangslogikk for ressurser
        som ble opprettet før OwnerUserId
        ble innført.
    */
    private async Task
        LinkLegacyResourcesToIdentityUserAsync(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        List<Resource> legacyResources =
            await _context.Resources
                .Where(
                    resource =>
                        resource.OwnerUserId
                            == null

                        &&

                        resource.OwnerEmail
                            == user.Email)
                .ToListAsync();

        if (legacyResources.Count == 0)
        {
            return;
        }

        foreach (Resource resource in legacyResources)
        {
            resource.OwnerUserId = user.Id;
        }

        await _context
            .SaveChangesAsync();
    }

    /*
        Gjør tom eller whitespace-tekst om til null.
        Ellers fjernes unødvendige mellomrom
        foran og bak teksten.
    */
    private static string? NormalizeOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    #endregion
}
