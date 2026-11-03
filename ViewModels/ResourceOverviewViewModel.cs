using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Representerer en ressurs som vises
/// i ressursoversikten.
/// </summary>
public class ResourceOverviewViewModel
{
    public int Id { get; init; }

    public string Name { get; init; }
    = string.Empty;

    public ResourceType ResourceType { get; init; }

    public string ResourceName { get; init; }
    = string.Empty;

    public int Quantity { get; init; } = 1;

    public string? Capacity { get; init; }

    public string? Availability { get; init; }

    public string? Address { get; init; }



    public string Description { get; init; }
        = string.Empty;

    public Coordinate Position { get; init; }

    public string? ContactEmail { get; init; }

    public string? ContactPhoneCountryCode { get; init; }

    public string? ContactPhoneNumber { get; init; }

    public string OwnerEmail { get; init; }
        = string.Empty;

    public DateTime CreatedAt { get; init; }
}