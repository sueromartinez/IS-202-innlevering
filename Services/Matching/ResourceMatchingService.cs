/*
    ResourceMatchingService.cs – finner ressurser til et behov (og behov til en ressurs)

    Hva filen gjør: Kjernen i matchingen. Brukes av «Finn ressurser», «Behov jeg kan hjelpe med»
    og Sami, så alle tre gir samme svar.
    Slik fungerer søket, i to steg:
    1) Databasen gjør et grovt utvalg: samme type, tilgjengelig, og innenfor en firkant rundt
       punktet.
    2) C# regner ut nøyaktig avstand (Haversine-formelen), tar bort det som er utenfor radiusen,
       og sorterer nærmest først.
    Hvorfor to steg: den nøyaktige formelen kan ikke kjøres i databasen, og å hente alle
    ressurser hver gang ville blitt tregt. Firkanten fjerner det meste før vi regner.

    På nettsiden: Listen «Passende ressurser» med "X km unna", sortert nærmest først, og
    markørene på kartet.

    Oppgavekrav: 10, 22
*/

using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.Services.Matching;

/*
    Et søkeresultat: ressursen og avstanden i km.

    Representerer en ressurs som passer et registrert behov,
    med ferdig beregnet avstand i kilometer.

    Resultatet utelater telefonnummer og e-postadresse.
    Address beholdes for eksisterende funksjonalitet,
    men må vurderes før data sendes til en ekstern AI-tjeneste.

    ResourceId: Ressursens identifikator.
    ResourceType: Ressursens kategori.
    ResourceName: Navnet på ressursen.
    Description: Beskrivelse av ressursen.
    Quantity: Antall tilgjengelige enheter.
    Capacity: Oppgitt kapasitet.
    Availability: Oppgitt tilgjengelighet.
    Address: Ressursens registrerte adresse.
    DistanceKm: Avstand i kilometer, avrundet til én desimal.
*/
public record ResourceCandidate(
    int ResourceId,
    ResourceType ResourceType,
    string ResourceName,
    string Description,
    int Quantity,
    string? Capacity,
    string? Availability,
    string? Address,
    double DistanceKm);

/*
    Representerer et åpent behov som en ressursleverandør
    kan bidra til å dekke.

    Kandidaten inneholder beregnet avstand, men ikke
    behovets eksakte koordinater eller registrerte eier.
    Beskrivelsen kan likevel inneholde brukerregistrerte
    opplysninger og skal håndteres deretter.

    NeedId: Behovets identifikator.
    ResourceType: Ressurstypen som etterspørres.
    Priority: Behovets prioritet.
    Status: Behovets gjeldende status.
    Quantity: Etterspurt antall.
    Description: Beskrivelsen av behovet.
    DistanceKm: Avstand til nærmeste passende ressurs.
    MatchingResourceId:
    Identifikatoren til ressursleverandørens nærmeste passende ressurs.

    MatchingResourceName:
    Navnet på ressursleverandørens nærmeste passende ressurs.
*/
public record NeedCandidate(
    int NeedId,
    ResourceType ResourceType,
    NeedPriority Priority,
    NeedStatus Status,
    int Quantity,
    string Description,
    double DistanceKm,
    int MatchingResourceId,
    string MatchingResourceName);

/*
    Utfører deterministisk matching mellom registrerte
    behov og tilgjengelige ressurser.

    Databaseoperasjoner utføres gjennom
    IResourceMatchingRepository.

    Geografiske avstander beregnes med GeoDistance
    og Coordinate, uavhengig av AI-assistenten Sami.
*/
public class ResourceMatchingService
{
    #region Felt og konstruktør

    /*
        Standard søkeradius er 50 km. Radiusen holdes mellom 1 og 500 km, og vi viser maks 25
        treff.
    */
    public const double DefaultRadiusKm = 50;

    private const double MinRadiusKm = 1;

    private const double MaxRadiusKm = 500;

    private const int MaxResultsLimit = 25;

    private readonly IResourceMatchingRepository _repository;

    /*
        Tjenesten kjenner bare interfacet, ikke EF Core. I testene (MatchingTests.cs) kjøres den
        mot en database i minnet, uten MariaDB.

        Oppretter matchingservicen med datatilgang gjennom
        et repository-interface.

        repository:
        Repositoryet som henter behov og ressurser.
    */
    public ResourceMatchingService(IResourceMatchingRepository repository)
    {
        _repository = repository;
    }

    #endregion

    #region Ressurser til et behov

    /*
        Ressurser til ett behov: gitt en plassering og en type, finn nærmeste ledige ressurser.

        Finner tilgjengelige ressurser i nærheten av en
        geografisk posisjon, sortert etter avstand.

        latitude: Søkepunktets breddegrad.
        longitude: Søkepunktets lengdegrad.
        radiusKm: Maksimal søkeavstand i kilometer.
        types:
        Ressurskategorier som skal inkluderes.
        Null eller tom samling betyr alle kategorier.

        maxResults: Maksimalt antall resultater.
        excludeRequestedForNeedId:
        Ekskluderer ressurser som allerede er forespurt
        eller bekreftet for det oppgitte behovet.

        Returnerer:
        Tilgjengelige ressurskandidater innenfor radius.
    */
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

        Coordinate searchPosition = new(latitude, longitude);

        IReadOnlyList<Resource> resources =
            await _repository.GetAvailableResourcesNearAsync(
                searchPosition,
                radiusKm,
                types,
                excludeRequestedForNeedId);

        /*
            Databasefiltreringen er grov.
            Den nøyaktige avstanden beregnes etterpå.

            Filtrering skjer på uavrundet avstand, slik at
            en ressurs like utenfor radius ikke blir inkludert
            fordi visningsavstanden rundes ned.
        */
        List<ResourceCandidate> candidates = resources
            .Select(resource => new
            {
                Resource = resource,
                DistanceKm = CalculateDistanceKm(searchPosition, resource.Position)
            })
            /*
                Steg 2: den nøyaktige avstanden. Vi sammenligner med den uavrundede avstanden og
                runder først av når den skal vises, ellers kunne en ressurs 50,04 km unna kommet
                med i et 50 km-søk.
            */
            .Where(item => double.IsFinite(item.DistanceKm) && item.DistanceKm <= radiusKm)
            /*
                Nærmest først. Er to like langt unna, sorteres de på ID, så rekkefølgen alltid
                er den samme.
            */
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

    #region Behov til en frivillig

    /*
        Motsatt vei, for frivillige: ta mine ledige ressurser, finn åpne behov av samme type, og
        regn avstanden fra hvert behov til min nærmeste passende ressurs.

        Finner åpne behov som samsvarer med en
        ressursleverandørs tilgjengelige ressurser.

        Behovet matches mot den nærmeste tilgjengelige
        ressursen av samme kategori.

        Akutte behov prioriteres foran planlagte behov.
        Innenfor samme prioritet sorteres etter avstand.

        Returtypen utelater behovets geografiske koordinater.

        userId:
        Identity-ID til ressursleverandøren.

        radiusKm:
        Maksimal avstand mellom behov og ressurs.

        maxResults:
        Maksimalt antall behov som returneres.

        Returnerer:
        Åpne behov som kan dekkes av leverandørens ressurser.
    */
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
            await _repository.GetAvailableResourcesForProviderAsync(userId);

        if (ownResources.Count == 0)
        {
            return new List<NeedCandidate>();
        }

        List<ResourceType> ownTypes = ownResources
            .Select(resource => resource.ResourceType)
            .Distinct()
            .ToList();

        IReadOnlyList<Need> openNeeds = await _repository.GetOpenNeedsForTypesAsync(ownTypes);

        List<NeedCandidate> result = new List<NeedCandidate>();

        foreach (Need need in openNeeds)
        {
            Coordinate needPosition = need.Position;

            /*
                Finn leverandørens nærmeste tilgjengelige
                ressurs av samme kategori som behovet.
            */
            var closest = ownResources
                .Where(resource => resource.ResourceType == need.ResourceType)
                .Select(resource => new
                {
                    Resource = resource,
                    DistanceKm = CalculateDistanceKm(needPosition, resource.Position)
                })
                .Where(item => double.IsFinite(item.DistanceKm))
                .OrderBy(item => item.DistanceKm)
                .ThenBy(item => item.Resource.Id)
                .FirstOrDefault();

            if (closest is null || closest.DistanceKm > radiusKm)
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
            // Akutte behov først, så de nærmeste, så de eldste.
            .OrderBy(candidate => candidate.Priority == NeedPriority.Acute ? 0 : 1)
            .ThenBy(candidate => candidate.DistanceKm)
            .ThenBy(candidate => candidate.NeedId)
            .Take(maxResults)
            .ToList();
    }

    #endregion

    #region Hjelpemetoder

    /*
        Beregner luftlinjeavstanden mellom to posisjoner.

        from: Startposisjon.
        to: Sluttposisjon.
        Returnerer: Avstanden i kilometer.
    */
    private static double CalculateDistanceKm(Coordinate from, Coordinate to)
    {
        /*
            GeoDistance beholder sin eksisterende signatur.
            Coordinate gjør at koordinatpar håndteres samlet
            i matchinglogikken.
        */
        return GeoDistance.HaversineKm(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
    }

    /*
        Begrens søkeradius til portalens tillatte intervall.

        radiusKm:
        Ønsket søkeradius i kilometer.

        Returnerer: En gyldig søkeradius.
    */
    private static double NormalizeRadius(double radiusKm)
    {
        if (!double.IsFinite(radiusKm))
        {
            return DefaultRadiusKm;
        }

        return Math.Clamp(radiusKm, MinRadiusKm, MaxRadiusKm);
    }

    #endregion
}
