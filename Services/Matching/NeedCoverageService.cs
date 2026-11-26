/*
    NeedCoverageService.cs – dekning

    Hva filen gjør: Teller hvor mange ledige ressurser av riktig type som finnes innen 50 km fra
    hvert behov.
    Ressurser med utløpt tilgjengelighet telles ikke.

    På nettsiden: Linjen «Behov uten passende ressurs innen 50 km» i situasjonsrapporten.

    Oppgavekrav: 10
*/

using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Services.Matching;

/*
    Teller hvor mange tilgjengelige ressurser av riktig type
    som finnes i nærheten av hvert behov.
    Brukes i situasjonsrapporten for å finne behov uten dekning.
*/
public class NeedCoverageService
{
    #region Felt og konstruktør

    public const double CoverageRadiusKm = 50;

    private readonly ApplicationDbContext _context;

    public NeedCoverageService(ApplicationDbContext context)
    {
        _context = context;
    }

    #endregion

    #region Dekning

    /*
        Antall tilgjengelige ressurser av samme type innen
        CoverageRadiusKm for hvert behov (nøkkel = behovets id).
        Henter ressursene én gang og teller i minnet.
    */
    public async Task<Dictionary<int, int>> CountNearbyResourcesAsync(IEnumerable<Need> needs)
    {
        DateTime now = DateTime.UtcNow;

        var resources =
            (await _context.Resources
                .AsNoTracking()
                .Where(resource => resource.Status == ResourceStatus.Available)
                .Select(resource => new
                {
                    resource.ResourceType,
                    resource.Latitude,
                    resource.Longitude,
                    resource.Status,
                    resource.Availability
                })
                .ToListAsync())
            // Ressurser med utløpt periode regnes ikke med.
            .Where(resource =>
                ResourceAvailability.EffectiveStatus(resource.Status, resource.Availability, now)
                == ResourceStatus.Available)
            .ToList();

        Dictionary<int, int> result = new Dictionary<int, int>();

        foreach (Need need in needs)
        {
            result[need.Id] =
                resources.Count(resource =>
                    resource.ResourceType == need.ResourceType
                    && GeoDistance.HaversineKm(
                        need.Latitude, need.Longitude,
                        resource.Latitude, resource.Longitude) <= CoverageRadiusKm);
        }

        return result;
    }

    #endregion
}
