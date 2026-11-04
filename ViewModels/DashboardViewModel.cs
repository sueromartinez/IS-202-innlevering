using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Data som vises på dashboardet til offentlig aktør.
/// Hentes fra databasen av HomeController.
/// </summary>
public class DashboardViewModel
{
    /// <summary>Alle registrerte behov, nyeste først.</summary>
    public List<Need> Needs { get; init; } = new();

    /// <summary>Alle registrerte ressurser, nyeste først.</summary>
    public List<Resource> Resources { get; init; } = new();

    /// <summary>Antall akutte behov.</summary>
    public int AcuteCount =>
        Needs.Count(need => need.Priority == NeedPriority.Acute);

    /// <summary>Antall planlagte behov.</summary>
    public int PlannedCount =>
        Needs.Count(need => need.Priority == NeedPriority.Planned);

    /// <summary>Antall registrerte ressurser.</summary>
    public int ResourceCount => Resources.Count;

    // Counts explicit statuses only; missing or older free text remains unspecified.
    public int AvailableCount => Resources.Count(resource =>
        string.Equals(resource.Availability?.Trim(), "Tilgjengelig", StringComparison.OrdinalIgnoreCase));

    public int UnavailableCount => Resources.Count(resource =>
        string.Equals(resource.Availability?.Trim(), "Ikke tilgjengelig", StringComparison.OrdinalIgnoreCase));

    public int UnspecifiedCount => ResourceCount - AvailableCount - UnavailableCount;
}
