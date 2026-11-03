using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Hvor akutt et behov er.
/// </summary>
public enum NeedPriority
{
    [Display(Name = "Akutt")]
    Urgent,

    [Display(Name = "Planlagt")]
    Planned
}


/// <summary>
/// Representerer et behov som en offentlig aktør
/// har registrert, og som lagres i databasen.
/// </summary>
public class Need
{
    /// <summary>
    /// Unik identifikator.
    /// </summary>
    public int Id { get; init; }


    /// <summary>
    /// Kort tittel, for eksempel "Strømbehov".
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Title { get; init; } = string.Empty;


    /// <summary>
    /// Hvilken type ressurs som trengs.
    /// Bruker samme enum som ressursene, slik at
    /// behov og ressurser enkelt kan sammenlignes.
    /// </summary>
    [Required]
    public ResourceType ResourceType { get; init; }


    [Required]
    [StringLength(500)]
    public string Description { get; init; } = string.Empty;


    /// <summary>
    /// Akutt eller planlagt.
    /// </summary>
    [Required]
    public NeedPriority Priority { get; init; }


    public double Latitude { get; init; }


    public double Longitude { get; init; }


    /// <summary>
    /// E-posten til den offentlige aktøren som registrerte behovet.
    /// Erstattes av Identity UserId senere, på samme måte som i Resource.
    /// </summary>
    [Required]
    [StringLength(150)]
    public string OwnerEmail { get; init; } = string.Empty;


    /// <summary>
    /// Tidspunkt behovet ble registrert.
    /// </summary>
    public DateTime CreatedAt { get; init; } =
        DateTime.UtcNow;
}