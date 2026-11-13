using System.Globalization;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.Services;

// Lagrer periode og sluttdato i det eksisterende tekstfeltet uten å endre databasen.
public static class ResourceAvailability
{
    public static IReadOnlyDictionary<int, string> Periods { get; } = new Dictionary<int, string>
    {
        [1] = "1 måned", [3] = "3 måneder", [6] = "6 måneder",
        [12] = "1 år", [24] = "2 år", [36] = "3 år"
    };

    public static string CreatePeriod(int months, DateTime registeredAt)
    {
        if (!Periods.ContainsKey(months)) throw new ArgumentOutOfRangeException(nameof(months));
        return $"{Periods[months]}; til {registeredAt.AddMonths(months):yyyy-MM-dd}";
    }

    public static DateOnly? GetEndDate(string? availability)
    {
        var parts = availability?.Split("; til ", StringSplitOptions.None);
        return parts?.Length == 2 && Periods.Values.Contains(parts[0])
            && DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ? date : null;
    }

    // En utløpt periode er utilgjengelig; eldre fritekst tolkes ikke som en oppdiktet periode.
    public static ResourceStatus EffectiveStatus(ResourceStatus status, string? availability, DateTime now)
    {
        var end = GetEndDate(availability);
        return end.HasValue && end.Value < DateOnly.FromDateTime(now)
            ? ResourceStatus.Unavailable : status;
    }

    // Bevarer dashboardets gamle tekster og filtre når nye registreringer bruker periodefeltet.
    public static string? DashboardStatus(Resource resource)
    {
        if (!GetEndDate(resource.Availability).HasValue) return resource.Availability;
        return EffectiveStatus(resource.Status, resource.Availability, DateTime.UtcNow) switch
        {
            ResourceStatus.Available => "Tilgjengelig",
            ResourceStatus.Unavailable => "Ikke tilgjengelig",
            _ => "Opptatt"
        };
    }
}
