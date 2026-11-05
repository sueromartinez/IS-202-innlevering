using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Matching;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Services.Ai;

public enum SamiRole
{
    Volunteer,
    PublicActor,
    Admin
}


/// <summary>
/// Hvem Sami snakker med. Rollen bestemmer hvilke verktøy
/// modellen får se og får lov til å bruke.
/// </summary>
public class SamiContext
{
    public string UserId { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public SamiRole Role { get; init; }
}


/// <summary>
/// Et forslag fra Sami om å koble en ressurs til et behov.
/// Vises som et kort i chatten med en "Send forespørsel"-knapp.
/// Ingenting lagres før et menneske trykker på knappen.
/// </summary>
public class SamiSuggestion
{
    public int NeedId { get; init; }

    public int ResourceId { get; init; }

    public string ResourceName { get; init; } = string.Empty;

    public string ResourceTypeName { get; init; } = string.Empty;

    public string NeedDescription { get; init; } = string.Empty;

    public double DistanceKm { get; init; }

    public string Reason { get; init; } = string.Empty;
}


/// <summary>
/// Verktøyene Sami kan bruke. All tilgang til data går gjennom her.
///
/// Sikkerhet:
/// - Hvert verktøy sjekker rollen på nytt, selv om modellen bare
///   får se verktøyene den har lov til (forsvar i dybden).
/// - Ingen verktøy returnerer kontaktinformasjon (e-post/telefon).
/// - Ingen verktøy endrer data. suggest_match lager bare et forslag.
/// </summary>
public class SamiTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Beholder æ, ø og å som vanlige tegn, så modellen leser dem riktig.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };


    private readonly ApplicationDbContext _context;

    private readonly ResourceMatchingService _matching;

    private readonly NominatimGeocoder _geocoder;


    public SamiTools(
        ApplicationDbContext context,
        ResourceMatchingService matching,
        NominatimGeocoder geocoder
    )
    {
        _context = context;
        _matching = matching;
        _geocoder = geocoder;
    }


    // =========================================================
    // VERKTØYDEFINISJONER
    // =========================================================

    private static readonly ChatToolDefinition GetPortalPages = Define(
        "get_portal_pages",
        "Lister sidene i portalen som brukeren har tilgang til, med lenke og forklaring. Bruk når brukeren spør hvor noe er eller hvordan de gjør noe.",
        """{ "type": "object", "properties": {} }""");

    private static readonly ChatToolDefinition GetResourceTypes = Define(
        "get_resource_types",
        "Lister ressurstypene i portalen (verdi og norsk navn). Bruk før find_nearby_resources for å velge riktige typer.",
        """{ "type": "object", "properties": {} }""");

    private static readonly ChatToolDefinition GetRegistrationRules = Define(
        "get_registration_rules",
        "Gir feltene og valideringsreglene i registreringsskjemaet brukeren bruker (ressurs for frivillige, behov for offentlige aktører).",
        """{ "type": "object", "properties": {} }""");

    private static readonly ChatToolDefinition GetMyResources = Define(
        "get_my_resources",
        "Lister ressursene brukeren selv har registrert, med type og status.",
        """{ "type": "object", "properties": {} }""");

    private static readonly ChatToolDefinition FindNeedsICanHelpWith = Define(
        "find_needs_i_can_help_with",
        "Finner åpne behov som passer brukerens egne tilgjengelige ressurser. Returnerer bare omtrentlig avstand, ikke eksakt sted.",
        """
        {
          "type": "object",
          "properties": {
            "radius_km": { "type": "number", "description": "Søkeradius i km. Standard 50." }
          }
        }
        """);

    private static readonly ChatToolDefinition GetOpenNeeds = Define(
        "get_open_needs",
        "Lister registrerte behov som ikke er løst (status Ny, Vurderes eller Tildelt), med id, type, prioritet og beskrivelse.",
        """
        {
          "type": "object",
          "properties": {
            "resource_type": { "type": "string", "description": "Valgfritt: bare behov av denne ressurstypen (verdi fra get_resource_types)." }
          }
        }
        """);

    private static readonly ChatToolDefinition GetNeedDetails = Define(
        "get_need_details",
        "Henter ett behov med posisjon, status og forespørsler som allerede er sendt.",
        """
        {
          "type": "object",
          "properties": {
            "need_id": { "type": "integer", "description": "Id til behovet." }
          },
          "required": ["need_id"]
        }
        """);

    private static readonly ChatToolDefinition GeocodePlace = Define(
        "geocode_place",
        "Gjør et stedsnavn i Norge om til koordinater, f.eks. 'Grimstad sentrum'. Bruk når brukeren beskriver en hendelse et sted.",
        """
        {
          "type": "object",
          "properties": {
            "place": { "type": "string", "description": "Stedsnavn eller adresse." }
          },
          "required": ["place"]
        }
        """);

    private static readonly ChatToolDefinition FindNearbyResources = Define(
        "find_nearby_resources",
        "Finner TILGJENGELIGE ressurser nær et punkt eller et behov, sortert etter avstand (beregnet av systemet). Oppgi enten need_id, eller latitude og longitude.",
        """
        {
          "type": "object",
          "properties": {
            "need_id": { "type": "integer", "description": "Søk rundt dette behovet. Uten resource_types brukes behovets type." },
            "latitude": { "type": "number" },
            "longitude": { "type": "number" },
            "radius_km": { "type": "number", "description": "Søkeradius i km. Standard 50." },
            "resource_types": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Ressurstyper (verdier fra get_resource_types). Tom = alle typer."
            },
            "max_results": { "type": "integer", "description": "Maks antall treff, standard 8." }
          }
        }
        """);

    private static readonly ChatToolDefinition SuggestMatch = Define(
        "suggest_match",
        "Foreslår å koble en ressurs til et registrert behov. Brukeren får et kort med knappen 'Send forespørsel'. Ingenting lagres før brukeren trykker. Bruk bare id-er du har fått fra andre verktøy.",
        """
        {
          "type": "object",
          "properties": {
            "need_id": { "type": "integer" },
            "resource_id": { "type": "integer" },
            "reason": { "type": "string", "description": "Kort begrunnelse (maks 1–2 setninger) som vises til brukeren og den frivillige." }
          },
          "required": ["need_id", "resource_id", "reason"]
        }
        """);

    private static readonly ChatToolDefinition GetStatistics = Define(
        "get_statistics",
        "Gir tall for hele portalen: antall ressurser per type og status, behov per status og antall brukere.",
        """{ "type": "object", "properties": {} }""");


    /// <summary>
    /// Verktøyene hver rolle får tilgang til.
    /// </summary>
    public IReadOnlyList<ChatToolDefinition> GetDefinitions(SamiRole role)
    {
        return role switch
        {
            SamiRole.Volunteer => new[]
            {
                GetPortalPages, GetResourceTypes, GetRegistrationRules,
                GetMyResources, FindNeedsICanHelpWith
            },

            SamiRole.PublicActor => new[]
            {
                GetPortalPages, GetResourceTypes, GetRegistrationRules,
                GetOpenNeeds, GetNeedDetails, GeocodePlace,
                FindNearbyResources, SuggestMatch
            },

            _ => new[]
            {
                GetPortalPages, GetResourceTypes, GetRegistrationRules,
                GetOpenNeeds, GetNeedDetails, GeocodePlace,
                FindNearbyResources, SuggestMatch, GetStatistics
            }
        };
    }


    // =========================================================
    // UTFØRING
    // =========================================================

    /// <summary>
    /// Utfører et verktøykall og returnerer resultatet som JSON-tekst
    /// som sendes tilbake til modellen.
    /// </summary>
    public async Task<string> ExecuteAsync(
        ChatToolCall call,
        SamiContext context,
        List<SamiSuggestion> suggestions,
        CancellationToken cancellationToken
    )
    {
        bool allowed =
            GetDefinitions(context.Role).Any(tool => tool.Name == call.Name);

        if (!allowed)
        {
            return Serialize(new { error = "Dette verktøyet er ikke tilgjengelig for brukeren." });
        }

        try
        {
            return call.Name switch
            {
                "get_portal_pages" => Serialize(PortalPages(context.Role)),
                "get_resource_types" => Serialize(ResourceTypes()),
                "get_registration_rules" => Serialize(RegistrationRules(context.Role)),
                "get_my_resources" => await MyResourcesAsync(context, cancellationToken),
                "find_needs_i_can_help_with" => await NeedsICanHelpWithAsync(call.Arguments, context),
                "get_open_needs" => await OpenNeedsAsync(call.Arguments, cancellationToken),
                "get_need_details" => await NeedDetailsAsync(call.Arguments, cancellationToken),
                "geocode_place" => await GeocodeAsync(call.Arguments, cancellationToken),
                "find_nearby_resources" => await NearbyResourcesAsync(call.Arguments, cancellationToken),
                "suggest_match" => await SuggestMatchAsync(call.Arguments, suggestions, cancellationToken),
                "get_statistics" => await StatisticsAsync(cancellationToken),
                _ => Serialize(new { error = "Ukjent verktøy." })
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Detaljer logges av SamiAssistantService; modellen får bare en kort melding.
            throw new SamiToolException(call.Name, exception);
        }
    }


    // =========================================================
    // NAVIGASJON OG REGISTRERING
    // =========================================================

    private static object PortalPages(SamiRole role)
    {
        var pages = new List<object>
        {
            new { title = "Oversikt", url = "/Home/Dashboard", description = "Startsiden etter innlogging." }
        };

        if (role == SamiRole.Volunteer)
        {
            pages.Add(new { title = "Mine ressurser", url = "/Resource/ResourceOverview", description = "Ressursene du har registrert." });
            pages.Add(new { title = "Registrer ressurs", url = "/Resource/RegisterResource", description = "Skjema for å registrere en ny ressurs du kan tilby." });
            pages.Add(new { title = "Forespørsler", url = "/Request", description = "Forespørsler om dine ressurser. Her bekrefter eller avslår du." });
            pages.Add(new { title = "Behov jeg kan hjelpe med", url = "/Request/MatchingNeeds", description = "Åpne behov som passer ressursene dine." });
        }
        else
        {
            pages.Add(new { title = "Behov", url = "/Need/NeedOverview", description = "Alle registrerte behov. Trykk «Finn ressurser» på et behov for å matche." });
            pages.Add(new { title = "Registrer behov", url = "/Need/RegisterNeed", description = "Skjema for å registrere ett eller flere behov." });
            pages.Add(new { title = "Ressurser", url = "/Resource/ResourceOverview", description = "Alle registrerte ressurser med kontaktinformasjon." });
        }

        if (role == SamiRole.Admin)
        {
            pages.Add(new { title = "Brukere", url = "/Admin/Users", description = "Administrer brukere: roller, deaktivering og opplåsing." });
            pages.Add(new { title = "Opprett offentlig aktør", url = "/Admin/CreatePublicActor", description = "Lag konto for en kommune, etat eller annen offentlig aktør." });
        }

        pages.Add(new { title = "Varsler", url = "/Notification", description = "Varsler om forespørsler og svar (klokken øverst)." });
        pages.Add(new { title = "Profil", url = "/Account/Profile", description = "Endre navn, e-post og telefonnummer." });

        return new { pages };
    }


    private static object ResourceTypes()
    {
        return new
        {
            resource_types = Enum.GetValues<ResourceType>()
                .Select(type => new { value = type.ToString(), name = EnumDisplay.Name(type) })
        };
    }


    private static object RegistrationRules(SamiRole role)
    {
        if (role == SamiRole.Volunteer)
        {
            return new
            {
                form = "Registrer ressurs",
                url = "/Resource/RegisterResource",
                fields = new object[]
                {
                    new { field = "Ressurstype", required = true, rule = "Velg fra listen (se get_resource_types)." },
                    new { field = "Navn på ressurs", required = true, rule = "Maks 100 tegn, f.eks. «Traktor med henger»." },
                    new { field = "Beskrivelse", required = true, rule = "Maks 500 tegn. Beskriv hva ressursen kan brukes til." },
                    new { field = "Antall", required = true, rule = "Minst 1." },
                    new { field = "Kapasitet", required = false, rule = "Maks 200 tegn, f.eks. «8 personer» eller «20 tonn»." },
                    new { field = "Tilgjengelighet", required = false, rule = "Maks 200 tegn, f.eks. «Hverdager etter kl. 16»." },
                    new { field = "Adresse / område", required = false, rule = "Maks 200 tegn." },
                    new { field = "Plassering i kartet", required = true, rule = "Klikk i kartet der ressursen står. Uten plassering kan skjemaet ikke sendes." },
                    new { field = "Kontakt-e-post", required = false, rule = "Gyldig e-post. Står den tom, brukes e-posten på kontoen." },
                    new { field = "Landkode og telefonnummer", required = false, rule = "Landkode +47, +45 eller +46. Norske og danske nummer har 8 sifre, svenske 7–10." }
                }
            };
        }

        return new
        {
            form = "Registrer behov",
            url = "/Need/RegisterNeed",
            note = "Flere behov kan registreres i samme skjema.",
            fields = new object[]
            {
                new { field = "Type behov", required = true, rule = "Velg ressurstype fra listen." },
                new { field = "Prioritet", required = true, rule = "Akutt eller Planlagt." },
                new { field = "Antall", required = true, rule = "Mellom 1 og 9999." },
                new { field = "Beskrivelse", required = true, rule = "Maks 500 tegn." },
                new { field = "Plassering i kartet", required = true, rule = "Klikk i kartet der behovet er." }
            }
        };
    }


    // =========================================================
    // FRIVILLIG
    // =========================================================

    private async Task<string> MyResourcesAsync(
        SamiContext context,
        CancellationToken cancellationToken
    )
    {
        var resources =
            await _context.Resources
                .AsNoTracking()
                .Where(resource => resource.OwnerUserId == context.UserId)
                .OrderBy(resource => resource.ResourceName)
                .ToListAsync(cancellationToken);

        return Serialize(new
        {
            count = resources.Count,
            resources = resources.Select(resource => new
            {
                id = resource.Id,
                name = resource.ResourceName,
                type = EnumDisplay.Name(resource.ResourceType),
                quantity = resource.Quantity,
                status = EnumDisplay.Name(resource.Status)
            })
        });
    }


    private async Task<string> NeedsICanHelpWithAsync(
        JsonElement arguments,
        SamiContext context
    )
    {
        double radiusKm = GetDouble(arguments, "radius_km") ?? ResourceMatchingService.DefaultRadiusKm;

        List<NeedCandidate> needs =
            await _matching.FindNeedsForProviderAsync(context.UserId, radiusKm);

        return Serialize(new
        {
            radius_km = radiusKm,
            count = needs.Count,
            page_url = "/Request/MatchingNeeds",
            needs = needs.Select(need => new
            {
                type = EnumDisplay.Name(need.ResourceType),
                priority = EnumDisplay.Name(need.Priority),
                quantity = need.Quantity,
                description = need.Description,
                approx_distance_km = need.DistanceKm,
                matches_your_resource = need.MatchingResourceName
            })
        });
    }


    // =========================================================
    // OFFENTLIG AKTØR / ADMIN
    // =========================================================

    private async Task<string> OpenNeedsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        IQueryable<Need> query =
            _context.Needs
                .AsNoTracking()
                .Where(need => need.Status != NeedStatus.Resolved);

        ResourceType? type = ParseResourceType(GetString(arguments, "resource_type"));

        if (type.HasValue)
        {
            ResourceType filter = type.Value;
            query = query.Where(need => need.ResourceType == filter);
        }

        var needs =
            await query
                .OrderBy(need => need.Priority)
                .ThenByDescending(need => need.CreatedAt)
                .Take(30)
                .ToListAsync(cancellationToken);

        return Serialize(new
        {
            count = needs.Count,
            needs = needs.Select(need => new
            {
                id = need.Id,
                type = need.ResourceType.ToString(),
                type_name = EnumDisplay.Name(need.ResourceType),
                priority = EnumDisplay.Name(need.Priority),
                status = EnumDisplay.Name(need.Status),
                quantity = need.Quantity,
                description = need.Description,
                registered = need.CreatedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
                match_page_url = $"/Match/Need/{need.Id}"
            })
        });
    }


    private async Task<string> NeedDetailsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        int? needId = GetInt(arguments, "need_id");

        Need? need =
            needId is null
                ? null
                : await _context.Needs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(n => n.Id == needId.Value, cancellationToken);

        if (need is null)
        {
            return Serialize(new { error = "Fant ikke behovet. Bruk get_open_needs for å se gyldige id-er." });
        }

        var assignments =
            await _context.NeedAssignments
                .AsNoTracking()
                .Where(a => a.NeedId == need.Id)
                .Select(a => new
                {
                    ResourceId = a.ResourceId,
                    ResourceName = a.Resource!.ResourceName,
                    a.Status
                })
                .ToListAsync(cancellationToken);

        return Serialize(new
        {
            id = need.Id,
            type = need.ResourceType.ToString(),
            type_name = EnumDisplay.Name(need.ResourceType),
            priority = EnumDisplay.Name(need.Priority),
            status = EnumDisplay.Name(need.Status),
            quantity = need.Quantity,
            description = need.Description,
            latitude = need.Latitude,
            longitude = need.Longitude,
            match_page_url = $"/Match/Need/{need.Id}",
            requests_sent = assignments.Select(a => new
            {
                resource_id = a.ResourceId,
                resource_name = a.ResourceName,
                status = EnumDisplay.Name(a.Status)
            })
        });
    }


    private async Task<string> GeocodeAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        string? place = GetString(arguments, "place");

        if (string.IsNullOrWhiteSpace(place))
        {
            return Serialize(new { found = false, error = "Mangler stedsnavn." });
        }

        GeocodeResult? result = await _geocoder.GeocodeAsync(place, cancellationToken);

        if (result is null)
        {
            return Serialize(new
            {
                found = false,
                error = "Fant ikke stedet. Be brukeren om et mer presist sted, eller bruk et registrert behov."
            });
        }

        return Serialize(new
        {
            found = true,
            place = result.DisplayName,
            latitude = Math.Round(result.Latitude, 5),
            longitude = Math.Round(result.Longitude, 5)
        });
    }


    private async Task<string> NearbyResourcesAsync(
        JsonElement arguments,
        CancellationToken cancellationToken
    )
    {
        int? needId = GetInt(arguments, "need_id");
        double? latitude = GetDouble(arguments, "latitude");
        double? longitude = GetDouble(arguments, "longitude");
        double radiusKm = GetDouble(arguments, "radius_km") ?? ResourceMatchingService.DefaultRadiusKm;
        int maxResults = GetInt(arguments, "max_results") ?? 8;

        List<ResourceType> types =
            GetStringArray(arguments, "resource_types")
                .Select(ParseResourceType)
                .Where(type => type.HasValue)
                .Select(type => type!.Value)
                .Distinct()
                .ToList();

        if (needId.HasValue)
        {
            Need? need =
                await _context.Needs
                    .AsNoTracking()
                    .FirstOrDefaultAsync(n => n.Id == needId.Value, cancellationToken);

            if (need is null)
            {
                return Serialize(new { error = "Fant ikke behovet." });
            }

            latitude = need.Latitude;
            longitude = need.Longitude;

            if (types.Count == 0)
            {
                types.Add(need.ResourceType);
            }
        }

        if (latitude is null || longitude is null)
        {
            return Serialize(new
            {
                error = "Oppgi need_id, eller latitude og longitude (bruk geocode_place for et stedsnavn)."
            });
        }

        List<ResourceCandidate> candidates =
            await _matching.FindNearbyResourcesAsync(
                latitude.Value,
                longitude.Value,
                radiusKm,
                types,
                maxResults,
                excludeRequestedForNeedId: needId);

        return Serialize(new
        {
            searched_types = types.Count == 0
                ? new List<string> { "alle typer" }
                : types.Select(type => EnumDisplay.Name(type)).ToList(),
            radius_km = radiusKm,
            count = candidates.Count,
            note = candidates.Count == 0
                ? "Ingen tilgjengelige ressurser funnet. Foreslå større radius eller andre ressurstyper."
                : "Avstand er luftlinje beregnet av systemet. Bruk disse tallene, ikke egne anslag.",
            resources = candidates.Select(candidate => new
            {
                id = candidate.ResourceId,
                name = candidate.ResourceName,
                type = EnumDisplay.Name(candidate.ResourceType),
                description = candidate.Description,
                quantity = candidate.Quantity,
                capacity = candidate.Capacity,
                availability = candidate.Availability,
                area = candidate.Address,
                distance_km = candidate.DistanceKm
            })
        });
    }


    private async Task<string> SuggestMatchAsync(
        JsonElement arguments,
        List<SamiSuggestion> suggestions,
        CancellationToken cancellationToken
    )
    {
        int? needId = GetInt(arguments, "need_id");
        int? resourceId = GetInt(arguments, "resource_id");
        string reason = GetString(arguments, "reason") ?? string.Empty;

        if (needId is null || resourceId is null)
        {
            return Serialize(new { ok = false, error = "need_id og resource_id må oppgis." });
        }

        Need? need =
            await _context.Needs
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == needId.Value, cancellationToken);

        Resource? resource =
            await _context.Resources
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == resourceId.Value, cancellationToken);

        if (need is null || resource is null)
        {
            return Serialize(new { ok = false, error = "Behovet eller ressursen finnes ikke. Bruk bare id-er fra verktøyene." });
        }

        if (need.Status == NeedStatus.Resolved)
        {
            return Serialize(new { ok = false, error = "Behovet er allerede løst." });
        }

        if (resource.Status != ResourceStatus.Available)
        {
            return Serialize(new { ok = false, error = "Ressursen er ikke tilgjengelig." });
        }

        bool alreadyRequested =
            await _context.NeedAssignments.AnyAsync(a =>
                a.NeedId == need.Id
                && a.ResourceId == resource.Id
                && (a.Status == AssignmentStatus.Requested || a.Status == AssignmentStatus.Confirmed),
                cancellationToken);

        if (alreadyRequested)
        {
            return Serialize(new { ok = false, error = "Denne ressursen er allerede spurt om behovet." });
        }

        if (suggestions.All(s => !(s.NeedId == need.Id && s.ResourceId == resource.Id)))
        {
            suggestions.Add(new SamiSuggestion
            {
                NeedId = need.Id,
                ResourceId = resource.Id,
                ResourceName = resource.ResourceName,
                ResourceTypeName = EnumDisplay.Name(resource.ResourceType),
                NeedDescription = need.Description,
                DistanceKm = Math.Round(
                    GeoDistance.HaversineKm(need.Latitude, need.Longitude, resource.Latitude, resource.Longitude),
                    1),
                Reason = reason.Length > 300 ? reason[..300] : reason
            });
        }

        return Serialize(new
        {
            ok = true,
            note = "Forslaget vises som et kort med knappen «Send forespørsel». Ikke si at forespørselen er sendt."
        });
    }


    private async Task<string> StatisticsAsync(CancellationToken cancellationToken)
    {
        var resourcesByType =
            await _context.Resources
                .AsNoTracking()
                .GroupBy(resource => new { resource.ResourceType, resource.Status })
                .Select(group => new { group.Key.ResourceType, group.Key.Status, Count = group.Count() })
                .ToListAsync(cancellationToken);

        var needsByStatus =
            await _context.Needs
                .AsNoTracking()
                .GroupBy(need => need.Status)
                .Select(group => new { Status = group.Key, Count = group.Count() })
                .ToListAsync(cancellationToken);

        int userCount = await _context.Users.CountAsync(cancellationToken);

        return Serialize(new
        {
            users = userCount,
            resources = resourcesByType.Select(row => new
            {
                type = EnumDisplay.Name(row.ResourceType),
                status = EnumDisplay.Name(row.Status),
                count = row.Count
            }),
            needs = needsByStatus.Select(row => new
            {
                status = EnumDisplay.Name(row.Status),
                count = row.Count
            })
        });
    }


    // =========================================================
    // HJELPEMETODER
    // =========================================================

    private static ChatToolDefinition Define(string name, string description, string schemaJson)
    {
        return new ChatToolDefinition
        {
            Name = name,
            Description = description,
            Parameters = JsonNode.Parse(schemaJson)!.AsObject()
        };
    }


    private static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }


    /// <summary>
    /// Godtar både "Boat", "boat" og det norske navnet "Båt".
    /// Små modeller blander ofte disse.
    /// </summary>
    private static ResourceType? ParseResourceType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out ResourceType parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        foreach (ResourceType type in Enum.GetValues<ResourceType>())
        {
            if (string.Equals(EnumDisplay.Name(type), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        return null;
    }


    private static string? GetString(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }


    /// <summary>
    /// Leser et tall, også når modellen sender det som tekst ("12").
    /// </summary>
    private static double? GetDouble(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return double.IsFinite(number) ? number : null;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(
                value.GetString()?.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsed)
            && double.IsFinite(parsed))
        {
            return parsed;
        }

        return null;
    }


    private static int? GetInt(JsonElement arguments, string name)
    {
        double? value = GetDouble(arguments, name);

        return value is null ? null : (int)Math.Round(value.Value);
    }


    private static List<string> GetStringArray(JsonElement arguments, string name)
    {
        var result = new List<string>();

        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(name, out JsonElement value))
        {
            return result;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is string text)
                {
                    result.Add(text);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.String && value.GetString() is string single)
        {
            // Noen modeller sender "Boat, Generator" i stedet for en liste.
            result.AddRange(single.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return result;
    }
}


/// <summary>
/// Et verktøy feilet (f.eks. databasefeil). Fanges i SamiAssistantService,
/// som logger detaljene og gir modellen en kort feilmelding.
/// </summary>
public class SamiToolException : Exception
{
    public SamiToolException(string toolName, Exception inner)
        : base($"Verktøyet {toolName} feilet.", inner)
    {
        ToolName = toolName;
    }

    public string ToolName { get; }
}
