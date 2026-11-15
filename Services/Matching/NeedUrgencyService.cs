using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Services.Matching;

public enum UrgencyLevel
{
    Low,
    Medium,
    High,
    Critical
}


/// <summary>
/// Hastegraden til ett behov: en poengsum fra 0 til 100,
/// et nivå, og en liste med grunner slik at alle kan se HVORFOR.
/// </summary>
public record NeedUrgency(
    int Score,
    UrgencyLevel Level,
    IReadOnlyList<string> Reasons,
    int NearbyResources
)
{
    public string LevelName => Level switch
    {
        UrgencyLevel.Critical => "Kritisk",
        UrgencyLevel.High => "Høy",
        UrgencyLevel.Medium => "Middels",
        _ => "Lav"
    };

    /// <summary>
    /// CSS-klasse for merket, f.eks. "urgency-critical".
    /// </summary>
    public string CssClass => "urgency-" + Level.ToString().ToLowerInvariant();
}


/// <summary>
/// Enkel, forklarbar risiko- og prioritetsmodell for behov.
///
/// Poengene settes sammen av fire ting som en kriseleder selv ville sett på:
///   1. Prioritet:   Akutt 50, Planlagt 15
///   2. Frist:       passert +35, innen 1 døgn +30, innen 3 døgn +20, innen 7 døgn +10
///   3. Ventetid:    ubehandlet i over 24 t +15, over 6 t +8, over 1 t +3
///   4. Dekning:     ingen tilgjengelige ressurser av riktig type innen 50 km +20, bare 1–2 +8
///
/// Behov som allerede er tildelt får bare 30 % av poengene, og løste behov får 0.
/// Nivå: 70+ Kritisk, 45–69 Høy, 25–44 Middels, under 25 Lav.
///
/// Modellen er bevisst enkel og deterministisk, slik at den kan forklares og testes.
/// </summary>
public class NeedUrgencyService
{
    public const double CoverageRadiusKm = 50;


    private readonly ApplicationDbContext _context;


    public NeedUrgencyService(ApplicationDbContext context)
    {
        _context = context;
    }


    /// <summary>
    /// Regner ut hastegrad for mange behov på én gang.
    /// Henter tilgjengelige ressurser én gang og teller i minnet.
    /// </summary>
    public async Task<Dictionary<int, NeedUrgency>> CalculateAsync(IEnumerable<Need> needs)
    {
        List<Need> needList = needs.ToList();
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

        var result = new Dictionary<int, NeedUrgency>();

        foreach (Need need in needList)
        {
            int nearby =
                resources.Count(resource =>
                    resource.ResourceType == need.ResourceType
                    && GeoDistance.HaversineKm(
                        need.Latitude, need.Longitude,
                        resource.Latitude, resource.Longitude) <= CoverageRadiusKm);

            result[need.Id] = Calculate(need, nearby, now);
        }

        return result;
    }


    /// <summary>
    /// Selve modellen. Ren funksjon uten database, så den er lett å teste.
    /// </summary>
    public static NeedUrgency Calculate(Need need, int nearbyResources, DateTime nowUtc)
    {
        if (need.Status == NeedStatus.Resolved)
        {
            return new NeedUrgency(0, UrgencyLevel.Low, new[] { "Behovet er løst." }, nearbyResources);
        }

        var reasons = new List<string>();
        double score = 0;

        // 1. Prioritet
        if (need.Priority == NeedPriority.Acute)
        {
            score += 50;
            reasons.Add("Akutt prioritet");
        }
        else
        {
            score += 15;
            reasons.Add("Planlagt prioritet");
        }

        // 2. Frist (lagres som dato; fristen gjelder ut dagen)
        if (need.Deadline.HasValue)
        {
            DateTime deadlineEnd = need.Deadline.Value.Date.AddDays(1);
            double hoursLeft = (deadlineEnd - nowUtc).TotalHours;

            if (hoursLeft < 0)
            {
                score += 35;
                reasons.Add("Fristen er passert");
            }
            else if (hoursLeft <= 24)
            {
                score += 30;
                reasons.Add("Frist innen ett døgn");
            }
            else if (hoursLeft <= 72)
            {
                score += 20;
                reasons.Add("Frist innen tre døgn");
            }
            else if (hoursLeft <= 168)
            {
                score += 10;
                reasons.Add("Frist innen en uke");
            }
        }

        // 3. Ventetid (bare for behov som ikke er tildelt)
        if (need.Status is NeedStatus.New or NeedStatus.UnderReview)
        {
            double hoursWaiting = (nowUtc - need.CreatedAt).TotalHours;

            if (hoursWaiting > 24)
            {
                score += 15;
                reasons.Add("Har ventet over et døgn");
            }
            else if (hoursWaiting > 6)
            {
                score += 8;
                reasons.Add("Har ventet over 6 timer");
            }
            else if (hoursWaiting > 1)
            {
                score += 3;
            }
        }

        // 4. Dekning: finnes det ressurser i nærheten?
        if (nearbyResources == 0)
        {
            score += 20;
            reasons.Add($"Ingen tilgjengelige ressurser av riktig type innen {CoverageRadiusKm:0} km");
        }
        else if (nearbyResources <= 2)
        {
            score += 8;
            reasons.Add($"Bare {nearbyResources} tilgjengelig{(nearbyResources == 1 ? "" : "e")} ressurs{(nearbyResources == 1 ? "" : "er")} i nærheten");
        }

        // Tildelte behov er under kontroll.
        if (need.Status == NeedStatus.Assigned)
        {
            score *= 0.3;
            reasons.Add("Ressurs er bekreftet");
        }

        int finalScore = (int)Math.Round(Math.Clamp(score, 0, 100));

        UrgencyLevel level =
            finalScore >= 70 ? UrgencyLevel.Critical
            : finalScore >= 45 ? UrgencyLevel.High
            : finalScore >= 25 ? UrgencyLevel.Medium
            : UrgencyLevel.Low;

        return new NeedUrgency(finalScore, level, reasons, nearbyResources);
    }
}
