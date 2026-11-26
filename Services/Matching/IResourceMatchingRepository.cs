/*
    IResourceMatchingRepository.cs – hvilke data matchingen trenger

    Hva filen gjør: Et interface som sier HVA matchingen trenger av data, men ikke HVORDAN de
    hentes. Den ekte versjonen er EfResourceMatchingRepository (EF Core og MariaDB). Fordi
    matchingen bare kjenner interfacet, kan datatilgangen byttes ut uten å endre matchingen.
*/

using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.Services.Matching;

/*
    Definerer datatilgangen som ressursmatching trenger.
    Skjuler databaseimplementasjonen fra matchinglogikken.

    Grensesnittet gjør det mulig å erstatte EF Core med
    en annen datatilgangsimplementasjon uten å endre
    ResourceMatchingService.
*/
public interface IResourceMatchingRepository
{
    /*
        Henter tilgjengelige ressurser innenfor et geografisk
        søkeområde og filtrerer på eventuelle ressurstyper
        og eksisterende forespørsler.

        center: Søkeområdets midtpunkt.
        radiusKm: Søkeradius i kilometer.
        types:
        Ressurstyper som skal inkluderes. Null eller tom
        samling betyr alle typer.

        excludeRequestedForNeedId:
        Behovet som allerede forespurte eller bekreftede
        ressurser skal ekskluderes for.

        Returnerer:
        Tilgjengelige ressurser som passer databasefiltrene.
    */
    Task<IReadOnlyList<Resource>> GetAvailableResourcesNearAsync(
        Coordinate center,
        double radiusKm,
        IReadOnlyCollection<ResourceType>? types,
        int? excludeRequestedForNeedId);

    /*
        Henter alle tilgjengelige ressurser som tilhører
        en bestemt ressursleverandør.

        userId: Eierens Identity-ID.
        Returnerer: Brukerens tilgjengelige ressurser.
    */
    Task<IReadOnlyList<Resource>> GetAvailableResourcesForProviderAsync(string userId);

    /*
        Henter åpne behov som samsvarer med de oppgitte
        ressurskategoriene.

        types: Aktuelle ressurskategorier.
        Returnerer:
        Behov med status Ny eller Vurderes.
    */
    Task<IReadOnlyList<Need>> GetOpenNeedsForTypesAsync(IReadOnlyCollection<ResourceType> types);
}
