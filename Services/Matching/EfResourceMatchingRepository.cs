using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Services.Matching;

/// <summary>
/// Implementerer datatilgang for ressursmatching med
/// Entity Framework Core og ApplicationDbContext.
/// </summary>
/// <remarks>
/// Repositoryet utfører databasefiltrering.
/// Eksakte avstander, prioritering og rangering
/// håndteres av ResourceMatchingService.
/// </remarks>
public sealed class EfResourceMatchingRepository
    : IResourceMatchingRepository
{
    private readonly ApplicationDbContext _context;

    #region Konstruktør

    /// <summary>
    /// Oppretter repositoryet med databasekonteksten
    /// som leveres gjennom Dependency Injection.
    /// </summary>
    /// <param name="context">
    /// EF Core-konteksten for Beredskapsportalen.
    /// </param>
    public EfResourceMatchingRepository(
        ApplicationDbContext context)
    {
        _context = context;
    }

    #endregion

    #region Ressursspørringer

    /// <inheritdoc />
    public async Task<IReadOnlyList<Resource>>
        GetAvailableResourcesNearAsync(
            Coordinate center,
            double radiusKm,
            IReadOnlyCollection<ResourceType>? types,
            int? excludeRequestedForNeedId)
    {
        // BBOX reduserer antallet rader som må hentes
        // før nøyaktig Haversine-avstand beregnes.
        var box = GeoDistance.BoundingBox(
            center.Latitude,
            center.Longitude,
            radiusKm);

        // Lokale variabler brukes som SQL-parametere.
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
            ResourceType[] selectedTypes = types.ToArray();

            query = query.Where(resource =>
                selectedTypes.Contains(resource.ResourceType));
        }

        if (excludeRequestedForNeedId.HasValue)
        {
            int needId = excludeRequestedForNeedId.Value;

            // Ressurser som allerede er forespurt eller
            // bekreftet for samme behov skal ikke
            // foreslås på nytt.
            query = query.Where(resource =>
                !_context.NeedAssignments.Any(assignment =>
                    assignment.NeedId == needId
                    && assignment.ResourceId == resource.Id
                    && (
                        assignment.Status == AssignmentStatus.Requested
                        || assignment.Status == AssignmentStatus.Confirmed
                    )));
        }

        return await query.ToListAsync();
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<Need>>
        GetOpenNeedsForTypesAsync(
            IReadOnlyCollection<ResourceType> types)
    {
        if (types.Count == 0)
        {
            return Array.Empty<Need>();
        }

        ResourceType[] selectedTypes = types.ToArray();

        return await _context.Needs
            .AsNoTracking()
            .Where(need =>
                (
                    need.Status == NeedStatus.New
                    || need.Status == NeedStatus.UnderReview
                )
                && selectedTypes.Contains(need.ResourceType))
            .ToListAsync();
    }

    #endregion
}