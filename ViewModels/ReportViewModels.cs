using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Matching;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Én ressurs i utskrift og eksport.
/// Feltene er flate og ferdig oversatt, slik at CSV og JSON
/// blir lette å lese for andre systemer og i Excel.
/// </summary>
public class ResourceReportRow
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public ResourceType Type { get; init; }

    public string TypeName { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public string? Capacity { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Municipality { get; init; } = string.Empty;

    public string? Address { get; init; }

    public ResourceStatus Status { get; init; }

    public string StatusName { get; init; } = string.Empty;

    public DateOnly? AvailableUntil { get; init; }

    public string? OwnerName { get; init; }

    public string? ContactEmail { get; init; }

    public string? ContactPhone { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public DateTime RegisteredAt { get; init; }
}


/// <summary>
/// Utskriftsvennlig ressursliste per kommune.
/// </summary>
public class ResourceReportViewModel
{
    public string? Municipality { get; init; }

    public ResourceType? Type { get; init; }

    public bool OnlyAvailable { get; init; }

    public List<string> Municipalities { get; init; } = new();

    public List<ResourceReportGroup> Groups { get; init; } = new();

    public int TotalCount => Groups.Sum(group => group.Resources.Count);

    public DateTime GeneratedAt { get; init; }
}


public class ResourceReportGroup
{
    public string Municipality { get; init; } = string.Empty;

    public List<ResourceReportRow> Resources { get; init; } = new();
}


/// <summary>
/// Dekning per ressurstype: hvor mange åpne behov,
/// og hvor mange tilgjengelige ressurser som finnes.
/// </summary>
public class TypeCoverageRow
{
    public ResourceType Type { get; init; }

    public string TypeName { get; init; } = string.Empty;

    public int OpenNeeds { get; init; }

    public int AvailableResources { get; init; }

    public bool IsGap => OpenNeeds > 0 && AvailableResources == 0;

    public bool IsTight => OpenNeeds > 0 && AvailableResources > 0 && AvailableResources < OpenNeeds;
}


public class UrgentNeedRow
{
    public int Id { get; init; }

    public string Description { get; init; } = string.Empty;

    public string TypeName { get; init; } = string.Empty;

    public string PriorityName { get; init; } = string.Empty;

    public string StatusName { get; init; } = string.Empty;

    public bool IsAssigned { get; init; }

    public int Quantity { get; init; }

    public DateTime? Deadline { get; init; }

    public NeedUrgency Urgency { get; init; } = new(0, UrgencyLevel.Low, Array.Empty<string>(), 0);
}


public class MunicipalityRow
{
    public string Municipality { get; init; } = string.Empty;

    public int Available { get; init; }

    public int Total { get; init; }
}


/// <summary>
/// Situasjonsrapporten. Tallene regnes ut direkte fra databasen,
/// så rapporten virker også når språkmodellen er nede.
/// </summary>
public class SituationReportViewModel
{
    public DateTime GeneratedAt { get; init; }

    public int OpenNeeds { get; init; }

    public int AcuteOpenNeeds { get; init; }

    public int OverdueNeeds { get; init; }

    public int AssignedNeeds { get; init; }

    public int ResolvedNeeds { get; init; }

    public int CriticalNeeds { get; init; }

    public int HighNeeds { get; init; }

    public int MediumNeeds { get; init; }

    public int LowNeeds { get; init; }

    public int AvailableResources { get; init; }

    public int BusyResources { get; init; }

    public int TotalResources { get; init; }

    public int PendingRequests { get; init; }

    public int ConfirmedLast24h { get; init; }

    public List<TypeCoverageRow> Coverage { get; init; } = new();

    public List<UrgentNeedRow> MostUrgent { get; init; } = new();

    public List<UrgentNeedRow> Uncovered { get; init; } = new();

    public List<MunicipalityRow> Municipalities { get; init; } = new();

    /// <summary>
    /// Kort oppsummering laget uten AI (regelbasert).
    /// </summary>
    public List<string> AutomaticSummary { get; set; } = new();
}
