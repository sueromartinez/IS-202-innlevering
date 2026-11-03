using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Et behov registrert av en offentlig aktør.
/// Bruker samme ResourceType som ressursene,
/// slik at behov og ressurser kan matches senere.
/// </summary>
public class Need
{
    public int Id { get; init; }

    // Feltene under har "set" slik at behovet kan endres
    public ResourceType ResourceType { get; set; }

    public NeedPriority Priority { get; set; }

    public int Quantity { get; set; } = 1;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>
    /// Midlertidig, erstattes av Identity UserId senere.
    /// </summary>
    [StringLength(150)]
    public string OwnerEmail { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}


/// <summary>
/// Hvor raskt behovet må dekkes.
/// </summary>
public enum NeedPriority
{
    [Display(Name = "Akutt")]
    Acute,

    [Display(Name = "Planlagt")]
    Planned
}