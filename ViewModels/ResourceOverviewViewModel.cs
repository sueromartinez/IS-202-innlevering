/*
    ResourceOverviewViewModel.cs

    Hva filen gjør: Én ressurs slik den vises i ressursoversikten («Ressurser» i menyen).

    Oppgavekrav: 13, 18
*/

using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/*
    Representerer en ressurs som vises
    i ressursoversikten.
*/
public class ResourceOverviewViewModel
{
    public int Id { get; init; }

    public ResourceType ResourceType { get; init; }

    public string ResourceName { get; init; }
    = string.Empty;

    public int Quantity { get; init; } = 1;

    public string? Capacity { get; init; }

    public string? Availability { get; init; }

    // Bruker lagret status og periodens faktiske sluttdato i oversikten.
    public ResourceStatus EffectiveStatus => FirstWebAppInDocker.Services.ResourceAvailability
        .EffectiveStatus(Status, Availability, DateTime.UtcNow);

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

    public ResourceStatus Status { get; init; }
        = ResourceStatus.Available;
}
