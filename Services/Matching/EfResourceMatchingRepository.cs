/*
    EfResourceMatchingRepository.cs – databasespørringene til matchingen

    Hva filen gjør: Steg 1 i matchingen: det grove utvalget som kjøres i MariaDB.
*/

using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Services.Matching;

/*
    Implementerer datatilgang for ressursmatching med
    Entity Framework Core og ApplicationDbContext.

    Repositoryet utfører databasefiltrering.
    Eksakte avstander, prioritering og rangering
    håndteres av ResourceMatchingService.
*/
public sealed class EfResourceMatchingRepository
    : IResourceMatchingRepository
{
    #region Felt og konstruktør

    private readonly ApplicationDbContext _context;

    /*
        Oppretter repositoryet med databasekonteksten
        som leveres gjennom Dependency Injection.

        context:
        EF Core-konteksten for Beredskapsportalen.
    */
    public EfResourceMatchingRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    #endregion

    #region Ressursspørringer

    public async Task<IReadOnlyList<Resource>>
        GetAvailableResourcesNearAsync(
            Coordinate center,
            double radiusKm,
            IReadOnlyCollection<ResourceType>? types,
            int? excludeRequestedForNeedId)
    {
        /*
            Regner ut firkanten rundt søkepunktet.
        */
        (double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude) box =
            GeoDistance.BoundingBox(center.Latitude, center.Longitude, radiusKm);

        /*
            Verdiene legges i variabler, slik at EF Core sender dem som parametere til
            databasen. Det beskytter også mot SQL-injection.
        */
        double minLatitude = box.MinLatitude;
        double maxLatitude = box.MaxLatitude;
        double minLongitude = box.MinLongitude;
        double maxLongitude = box.MaxLongitude;

        IQueryable<Resource> query =
            _context.Resources
                .AsNoTracking()
                .Where(resource =>
                    /*
                        Bare ledige ressurser innenfor firkanten. Dette blir en WHERE-del i
                        SQL-en.
                    */
                    resource.Status == ResourceStatus.Available
                    && resource.Latitude >= minLatitude
                    && resource.Latitude <= maxLatitude
                    && resource.Longitude >= minLongitude
                    && resource.Longitude <= maxLongitude);

        if (types is { Count: > 0 })
        {
            ResourceType[] selectedTypes = types.ToArray();

            query = query.Where(resource => selectedTypes.Contains(resource.ResourceType));
        }

        if (excludeRequestedForNeedId.HasValue)
        {
            int needId = excludeRequestedForNeedId.Value;

            /*
                Ressurser som allerede er forespurt eller
                bekreftet for samme behov skal ikke
                foreslås på nytt.
            */
            query = query.Where(resource =>
                /*
                    Ressurser som allerede er spurt for dette behovet, tas bort, så de ikke
                    foreslås to ganger.
                */
                !_context.NeedAssignments.Any(assignment =>
                    assignment.NeedId == needId
                    && assignment.ResourceId == resource.Id
                    && (
                        assignment.Status == AssignmentStatus.Requested
                        || assignment.Status == AssignmentStatus.Confirmed)));
        }

        return await query.ToListAsync();
    }

    public async Task<IReadOnlyList<Resource>>
        GetAvailableResourcesForProviderAsync(string userId)
    {
        return await _context.Resources
            .AsNoTracking()
            .Where(resource =>
                resource.OwnerUserId == userId
                && resource.Status == ResourceStatus.Available)
            .ToListAsync();
    }

    #endregion

    #region Behovsspørringer

    public async Task<IReadOnlyList<Need>>
        GetOpenNeedsForTypesAsync(IReadOnlyCollection<ResourceType> types)
    {
        if (types.Count == 0)
        {
            return Array.Empty<Need>();
        }

        ResourceType[] selectedTypes = types.ToArray();

        return await _context.Needs
            .AsNoTracking()
            .Where(need =>
                (need.Status == NeedStatus.New || need.Status == NeedStatus.UnderReview)
                && selectedTypes.Contains(need.ResourceType))
            .ToListAsync();
    }

    #endregion
}
