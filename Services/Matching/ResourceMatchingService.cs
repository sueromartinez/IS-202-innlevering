using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.Services.Matching;

/// <summary>
/// Representerer en ressurs som passer et registrert behov,
/// med ferdig beregnet avstand i kilometer.
/// </summary>
/// <remarks>
/// Resultatet utelater telefonnummer og e-postadresse.
/// Address beholdes for eksisterende funksjonalitet,
/// men må vurderes før data sendes til en ekstern AI-tjeneste.
/// </remarks>
/// <param name="ResourceId">Ressursens identifikator.</param>
/// <param name="ResourceType">Ressursens kategori.</param>
/// <param name="ResourceName">Navnet på ressursen.</param>
/// <param name="Description">Beskrivelse av ressursen.</param>
/// <param name="Quantity">Antall tilgjengelige enheter.</param>
/// <param name="Capacity">Oppgitt kapasitet.</param>
/// <param name="Availability">Oppgitt tilgjengelighet.</param>
/// <param name="Address">Ressursens registrerte adresse.</param>
/// <param name="DistanceKm">Avstand i kilometer, avrundet til én desimal.</param>
public record ResourceCandidate(
    int ResourceId,
    ResourceType ResourceType,
    string ResourceName,
    string Description,
    int Quantity,
    string? Capacity,
    string? Availability,
    string? Address,
    double DistanceKm
);

/// <summary>
/// Representerer et åpent behov som en ressursleverandør
/// kan bidra til å dekke.
/// </summary>
/// <remarks>
/// Kandidaten inneholder beregnet avstand, men ikke
/// behovets eksakte koordinater eller registrerte eier.
/// Beskrivelsen kan likevel inneholde brukerregistrerte
/// opplysninger og skal håndteres deretter.
/// </remarks>
/// <param name="NeedId">Behovets identifikator.</param>
/// <param name="ResourceType">Ressurstypen som etterspørres.</param>
/// <param name="Priority">Behovets prioritet.</param>
/// <param name="Status">Behovets gjeldende status.</param>
/// <param name="Quantity">Etterspurt antall.</param>
/// <param name="Description">Beskrivelsen av behovet.</param>
/// <param name="DistanceKm">Avstand til nærmeste passende ressurs.</param>
/// <param name="MatchingResourceId">
/// Identifikatoren til ressursleverandørens nærmeste passende ressurs.
/// </param>
/// <param name="MatchingResourceName">
/// Navnet på ressursleverandørens nærmeste passende ressurs.
/// </param>
public record NeedCandidate(
    int NeedId,
    ResourceType ResourceType,
    NeedPriority Priority,
    NeedStatus Status,
    int Quantity,
    string Description,
    double DistanceKm,
    int MatchingResourceId,
    string MatchingResourceName
);

/// <summary>
/// Utfører deterministisk matching mellom registrerte
/// behov og tilgjengelige ressurser.
/// </summary>
/// <remarks>
/// Databaseoperasjoner utføres gjennom
/// IResourceMatchingRepository.
///
/// Geografiske avstander beregnes med GeoDistance
/// og Coordinate, uavhengig av AI-assistenten Sami.
/// </remarks>
public class ResourceMatchingService
{
    #region Konstanter og avhengigheter

    /// <summary>
    /// Standard søkeradius i kilometer.
    /// </summary>
    public const double DefaultRadiusKm = 50;

    private const double MinRadiusKm = 1;
    private const double MaxRadiusKm = 500;
    private const int MaxResultsLimit = 25;

    private readonly IResourceMatchingRepository _repository;

    #endregion

    #region Konstruktør

    /// <summary>
    /// Oppretter matchingservicen med datatilgang gjennom
    /// et repository-interface.
    /// </summary>
    /// <param name="repository">
    /// Repositoryet som henter behov og ressurser.
    /// </param>
    public ResourceMatchingService(
        IResourceMatchingRepository repository)
    {
        _repository = repository;
    }

    #endregion

    #region Matching av ressurser til behov

    /// <summary>
    /// Finner tilgjengelige ressurser i nærheten av en
    /// geografisk posisjon, sortert etter avstand.
    /// </summary>
    /// <param name="latitude">Søkepunktets breddegrad.</param>
    /// <param name="longitude">Søkepunktets lengdegrad.</param>
    /// <param name="radiusKm">Maksimal søkeavstand i kilometer.</param>
    /// <param name="types">
    /// Ressurskategorier som skal inkluderes.
    /// Null eller tom samling betyr alle kategorier.
    /// </param>
    /// <param name="maxResults">Maksimalt antall resultater.</param>
    /// <param name="excludeRequestedForNeedId">
    /// Ekskluderer ressurser som allerede er forespurt
    /// eller bekreftet for det oppgitte behovet.
    /// </param>
    /// <returns>
    /// Tilgjengelige ressurskandidater innenfor radius.
    /// </returns>
    public async Task<List<ResourceCandidate>> FindNearbyResourcesAsync(
        double latitude,
        double longitude,
        double radiusKm = DefaultRadiusKm,
        IReadOnlyCollection<ResourceType>? types = null,
        int maxResults = 10,
        int? excludeRequestedForNeedId = null)
    {
        radiusKm = NormalizeRadius(radiusKm);
        maxResults = Math.Clamp(maxResults, 1, MaxResultsLimit);

        Coordinate searchPosition = new(
            latitude,
            longitude);

        IReadOnlyList<Resource> resources =
            await _repository.GetAvailableResourcesNearAsync(
                searchPosition,
                radiusKm,
                types,
                excludeRequestedForNeedId);

        // Databasefiltreringen er grov.
        // Den nøyaktige avstanden beregnes etterpå.
        //
        // Filtrering skjer på uavrundet avstand, slik at
        // en ressurs like utenfor radius ikke blir inkludert
        // fordi visningsavstanden rundes ned.
        List<ResourceCandidate> candidates = resources
            .Select(resource => new
            {
                Resource = resource,
                DistanceKm = CalculateDistanceKm(
                    searchPosition,
                    resource.Position)
            })
            .Where(item =>
                double.IsFinite(item.DistanceKm)
                && item.DistanceKm <= radiusKm)
            .OrderBy(item => item.DistanceKm)
            .ThenBy(item => item.Resource.Id)
            .Take(maxResults)
            .Select(item => new ResourceCandidate(
                item.Resource.Id,
                item.Resource.ResourceType,
                item.Resource.ResourceName,
                item.Resource.Description,
                item.Resource.Quantity,
                item.Resource.Capacity,
                item.Resource.Availability,
                item.Resource.Address,
                Math.Round(item.DistanceKm, 1)))
            .ToList();

        return candidates;
    }

    #endregion

    #region Matching av behov til ressursleverandør

    /// <summary>
    /// Finner åpne behov som samsvarer med en
    /// ressursleverandørs tilgjengelige ressurser.
    /// </summary>
    /// <remarks>
    /// Behovet matches mot den nærmeste tilgjengelige
    /// ressursen av samme kategori.
    ///
    /// Akutte behov prioriteres foran planlagte behov.
    /// Innenfor samme prioritet sorteres etter avstand.
    ///
    /// Returtypen utelater behovets geografiske koordinater.
    /// </remarks>
    /// <param name="userId">
    /// Identity-ID til ressursleverandøren.
    /// </param>
    /// <param name="radiusKm">
    /// Maksimal avstand mellom behov og ressurs.
    /// </param>
    /// <param name="maxResults">
    /// Maksimalt antall behov som returneres.
    /// </param>
    /// <returns>
    /// Åpne behov som kan dekkes av leverandørens ressurser.
    /// </returns>
    public async Task<List<NeedCandidate>> FindNeedsForProviderAsync(
        string userId,
        double radiusKm = DefaultRadiusKm,
        int maxResults = 10)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new List<NeedCandidate>();
        }

        radiusKm = NormalizeRadius(radiusKm);
        maxResults = Math.Clamp(maxResults, 1, MaxResultsLimit);

        IReadOnlyList<Resource> ownResources =
            await _repository.GetAvailableResourcesForProviderAsync(
                userId);

        if (ownResources.Count == 0)
        {
            return new List<NeedCandidate>();
        }

        List<ResourceType> ownTypes = ownResources
            .Select(resource => resource.ResourceType)
            .Distinct()
            .ToList();

        IReadOnlyList<Need> openNeeds =
            await _repository.GetOpenNeedsForTypesAsync(
                ownTypes);

        var result = new List<NeedCandidate>();

        foreach (Need need in openNeeds)
        {
            Coordinate needPosition = need.Position;

            // Finn leverandørens nærmeste tilgjengelige
            // ressurs av samme kategori som behovet.
            var closest = ownResources
                .Where(resource =>
                    resource.ResourceType == need.ResourceType)
                .Select(resource => new
                {
                    Resource = resource,
                    DistanceKm = CalculateDistanceKm(
                        needPosition,
                        resource.Position)
                })
                .Where(item =>
                    double.IsFinite(item.DistanceKm))
                .OrderBy(item => item.DistanceKm)
                .ThenBy(item => item.Resource.Id)
                .FirstOrDefault();

            if (closest is null ||
                closest.DistanceKm > radiusKm)
            {
                continue;
            }

            result.Add(new NeedCandidate(
                need.Id,
                need.ResourceType,
                need.Priority,
                need.Status,
                need.Quantity,
                need.Description,
                Math.Round(closest.DistanceKm, 1),
                closest.Resource.Id,
                closest.Resource.ResourceName));
        }

        return result
            .OrderBy(candidate =>
                candidate.Priority == NeedPriority.Acute
                    ? 0
                    : 1)
            .ThenBy(candidate => candidate.DistanceKm)
            .ThenBy(candidate => candidate.NeedId)
            .Take(maxResults)
            .ToList();
    }

    #endregion

    #region Geografiske hjelpemetoder

    /// <summary>
    /// Beregner luftlinjeavstanden mellom to posisjoner.
    /// </summary>
    /// <param name="from">Startposisjon.</param>
    /// <param name="to">Sluttposisjon.</param>
    /// <returns>Avstanden i kilometer.</returns>
    private static double CalculateDistanceKm(
        Coordinate from,
        Coordinate to)
    {
        // GeoDistance beholder sin eksisterende signatur.
        // Coordinate gjør at koordinatpar håndteres samlet
        // i matchinglogikken.
        return GeoDistance.HaversineKm(
            from.Latitude,
            from.Longitude,
            to.Latitude,
            to.Longitude);
    }

    /// <summary>
    /// Begrens søkeradius til portalens tillatte intervall.
    /// </summary>
    /// <param name="radiusKm">
    /// Ønsket søkeradius i kilometer.
    /// </param>
    /// <returns>En gyldig søkeradius.</returns>
    private static double NormalizeRadius(double radiusKm)
    {
        if (!double.IsFinite(radiusKm))
        {
            return DefaultRadiusKm;
        }

        return Math.Clamp(
            radiusKm,
            MinRadiusKm,
            MaxRadiusKm);
    }

    #endregion
}