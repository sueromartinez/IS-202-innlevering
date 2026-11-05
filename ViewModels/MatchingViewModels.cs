using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Matching;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Siden "Finn ressurser" for ett behov.
/// </summary>
public class MatchNeedViewModel
{
    public Need Need { get; init; } = new();

    public double RadiusKm { get; init; }

    public List<ResourceCandidate> Candidates { get; init; } = new();

    public List<AssignmentRowViewModel> Assignments { get; init; } = new();
}


/// <summary>
/// En forespørsel slik den offentlige aktøren ser den,
/// med kontaktinformasjon til eieren.
/// </summary>
public class AssignmentRowViewModel
{
    public int Id { get; init; }

    public string ResourceName { get; init; } = string.Empty;

    public ResourceType ResourceType { get; init; }

    public AssignmentStatus Status { get; init; }

    public string? Message { get; init; }

    public bool SuggestedByAssistant { get; init; }

    public string? ContactEmail { get; init; }

    public string? ContactPhone { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime? RespondedAt { get; init; }
}


/// <summary>
/// En forespørsel slik den frivillige ser den.
/// Kontaktinfo til den offentlige aktøren vises først
/// etter at den frivillige har bekreftet.
/// </summary>
public class VolunteerRequestViewModel
{
    public int Id { get; init; }

    public string ResourceName { get; init; } = string.Empty;

    public ResourceType NeedType { get; init; }

    public NeedPriority NeedPriority { get; init; }

    public string NeedDescription { get; init; } = string.Empty;

    public int NeedQuantity { get; init; }

    public double DistanceKm { get; init; }

    public AssignmentStatus Status { get; init; }

    public string? Message { get; init; }

    public string? RequesterContact { get; init; }

    public DateTime CreatedAt { get; init; }
}


/// <summary>
/// Siden "Behov jeg kan hjelpe med".
/// </summary>
public class MatchingNeedsViewModel
{
    public double RadiusKm { get; init; }

    public bool HasAvailableResources { get; init; }

    public List<NeedCandidate> Needs { get; init; } = new();
}
