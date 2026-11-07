using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Services.Matching;

/// <summary>
/// En ressurs som passer et behov, med ferdig beregnet avstand.
/// Inneholder bevisst ingen kontaktinformasjon, slik at
/// objektet trygt kan sendes videre til språkmodellen.
/// </summary>
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
/// Et åpent behov som en frivillig kan hjelpe med.
/// Inneholder kun avstand, ikke eksakt posisjon eller
/// hvem som har registrert behovet.
/// </summary>
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
/// Deterministisk matching mellom behov og ressurser.
/// Brukes både av "Finn ressurser"-siden og av Sami.
/// </summary>
public class ResourceMatchingService
{
    public const double DefaultRadiusKm = 50;

    private const double MaxRadiusKm = 500;

    private const int MaxResultsLimit = 25;


    private readonly ApplicationDbContext _context;


    public ResourceMatchingService(ApplicationDbContext context)
    {
        _context = context;
    }


    /// <summary>
    /// Finner tilgjengelige ressurser nær et punkt, sortert etter avstand.
    /// </summary>
    /// <param name="types">Ressurstyper som er aktuelle. Null eller tom = alle typer.</param>
    /// <param name="excludeRequestedForNeedId">
    /// Hvis satt: hopp over ressurser som allerede er spurt
    /// eller tildelt dette behovet.
    /// </param>
    public async Task<List<ResourceCandidate>> FindNearbyResourcesAsync(
        double latitude,
        double longitude,
        double radiusKm = DefaultRadiusKm,
        IReadOnlyCollection<ResourceType>? types = null,
        int maxResults = 10,
        int? excludeRequestedForNeedId = null
    )
    {
        radiusKm = Math.Clamp(radiusKm, 1, MaxRadiusKm);
        maxResults = Math.Clamp(maxResults, 1, MaxResultsLimit);

        var box = GeoDistance.BoundingBox(latitude, longitude, radiusKm);

        // Kopieres til vanlige variabler, slik at EF Core
        // gjør dem om til SQL-parametere.
        double minLatitude = box.MinLatitude;
        double maxLatitude = box.MaxLatitude;
        double minLongitude = box.MinLongitude;
        double maxLongitude = box.MaxLongitude;

        IQueryable<Resource> query =
            _context.Resources
                .AsNoTracking()
                .Where(resource =>
                    resource.Status == ResourceStatus.Available
                    && resource.Latitude >= minLatitude
                    && resource.Latitude <= maxLatitude
                    && resource.Longitude >= minLongitude
                    && resource.Longitude <= maxLongitude);

        if (types is { Count: > 0 })
        {
            List<ResourceType> typeList = types.ToList();

            query = query.Where(resource =>
                typeList.Contains(resource.ResourceType));
        }

        if (excludeRequestedForNeedId.HasValue)
        {
            int needId = excludeRequestedForNeedId.Value;

            query = query.Where(resource =>
                !_context.NeedAssignments.Any(assignment =>
                    assignment.NeedId == needId
                    && assignment.ResourceId == resource.Id
                    && (assignment.Status == AssignmentStatus.Requested
                        || assignment.Status == AssignmentStatus.Confirmed)));
        }

        List<Resource> resources = await query.ToListAsync();

        // Nøyaktig avstand beregnes i C# etter grovfiltreringen.
        return resources
            .Select(resource => new ResourceCandidate(
                resource.Id,
                resource.ResourceType,
                resource.ResourceName,
                resource.Description,
                resource.Quantity,
                resource.Capacity,
                resource.Availability,
                resource.Address,
                Math.Round(
                    GeoDistance.HaversineKm(
                        latitude,
                        longitude,
                        resource.Latitude,
                        resource.Longitude),
                    1)))
            .Where(candidate => candidate.DistanceKm <= radiusKm)
            .OrderBy(candidate => candidate.DistanceKm)
            .Take(maxResults)
            .ToList();
    }


    /// <summary>
    /// Finner åpne behov som passer ressursene til en frivillig.
    /// Et behov passer når det har samme ressurstype som en av
    /// brukerens tilgjengelige ressurser og ligger innenfor radius.
    /// </summary>
    public async Task<List<NeedCandidate>> FindNeedsForProviderAsync(
        string userId,
        double radiusKm = DefaultRadiusKm,
        int maxResults = 10
    )
    {
        radiusKm = Math.Clamp(radiusKm, 1, MaxRadiusKm);
        maxResults = Math.Clamp(maxResults, 1, MaxResultsLimit);

        List<Resource> ownResources =
            await _context.Resources
                .AsNoTracking()
                .Where(resource =>
                    resource.OwnerUserId == userId
                    && resource.Status == ResourceStatus.Available)
                .ToListAsync();

        if (ownResources.Count == 0)
        {
            return new List<NeedCandidate>();
        }

        List<ResourceType> ownTypes =
            ownResources
                .Select(resource => resource.ResourceType)
                .Distinct()
                .ToList();

        List<Need> openNeeds =
            await _context.Needs
                .AsNoTracking()
                .Where(need =>
                    (need.Status == NeedStatus.New
                     || need.Status == NeedStatus.UnderReview)
                    && ownTypes.Contains(need.ResourceType))
                .ToListAsync();

        var result = new List<NeedCandidate>();

        foreach (Need need in openNeeds)
        {
            // Finn brukerens nærmeste ressurs av riktig type.
            var closest =
                ownResources
                    .Where(resource => resource.ResourceType == need.ResourceType)
                    .Select(resource => new
                    {
                        Resource = resource,
                        Distance = GeoDistance.HaversineKm(
                            need.Latitude,
                            need.Longitude,
                            resource.Latitude,
                            resource.Longitude)
                    })
                    .OrderBy(item => item.Distance)
                    .First();

            if (closest.Distance > radiusKm)
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
                Math.Round(closest.Distance, 1),
                closest.Resource.Id,
                closest.Resource.ResourceName));
        }

        // Akutte behov først, deretter nærmeste.
        return result
            .OrderBy(candidate => candidate.Priority == NeedPriority.Acute ? 0 : 1)
            .ThenBy(candidate => candidate.DistanceKm)
            .Take(maxResults)
            .ToList();
    }
}
