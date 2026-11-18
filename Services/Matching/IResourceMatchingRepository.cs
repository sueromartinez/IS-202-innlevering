using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.Services.Matching;

/// <summary>
/// Definerer datatilgangen som ressursmatching trenger.
/// Skjuler databaseimplementasjonen fra matchinglogikken.
/// </summary>
/// <remarks>
/// Grensesnittet gjør det mulig å erstatte EF Core med
/// en annen datatilgangsimplementasjon uten å endre
/// ResourceMatchingService.
/// </remarks>
public interface IResourceMatchingRepository
{
    /// <summary>
    /// Henter tilgjengelige ressurser innenfor et geografisk
    /// søkeområde og filtrerer på eventuelle ressurstyper
    /// og eksisterende forespørsler.
    /// </summary>
    /// <param name="center">Søkeområdets midtpunkt.</param>
    /// <param name="radiusKm">Søkeradius i kilometer.</param>
    /// <param name="types">
    /// Ressurstyper som skal inkluderes. Null eller tom
    /// samling betyr alle typer.
    /// </param>
    /// <param name="excludeRequestedForNeedId">
    /// Behovet som allerede forespurte eller bekreftede
    /// ressurser skal ekskluderes for.
    /// </param>
    /// <returns>
    /// Tilgjengelige ressurser som passer databasefiltrene.
    /// </returns>
    Task<IReadOnlyList<Resource>> GetAvailableResourcesNearAsync(
        Coordinate center,
        double radiusKm,
        IReadOnlyCollection<ResourceType>? types,
        int? excludeRequestedForNeedId);

    /// <summary>
    /// Henter alle tilgjengelige ressurser som tilhører
    /// en bestemt ressursleverandør.
    /// </summary>
    /// <param name="userId">Eierens Identity-ID.</param>
    /// <returns>Brukerens tilgjengelige ressurser.</returns>
    Task<IReadOnlyList<Resource>> GetAvailableResourcesForProviderAsync(
        string userId);

    /// <summary>
    /// Henter åpne behov som samsvarer med de oppgitte
    /// ressurskategoriene.
    /// </summary>
    /// <param name="types">Aktuelle ressurskategorier.</param>
    /// <returns>
    /// Behov med status Ny eller Vurderes.
    /// </returns>
    Task<IReadOnlyList<Need>> GetOpenNeedsForTypesAsync(
        IReadOnlyCollection<ResourceType> types);
}