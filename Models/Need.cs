
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Et behov registrert av en offentlig aktør.
///
/// Bruker samme ResourceType som ressursene,
/// slik at behov og ressurser kan matches senere.
///
/// ASP.NET Core Identity brukes for permanent
/// eierskap gjennom OwnerUserId.
/// </summary>
public class Need
{
    public int Id { get; init; }

    // ========================================
    // BEHOVSINFORMASJON
    // ========================================

    public ResourceType ResourceType { get; set; }

    public NeedPriority Priority { get; set; }

    public int Quantity { get; set; } = 1;

    [StringLength(500)]
    public string Description { get; set; } = string.Empty;


    // ========================================
    // PLASSERING
    // ========================================

    public double Latitude { get; set; }

    public double Longitude { get; set; }


    // ========================================
    // PERMANENT EIERSKAP MED IDENTITY
    // ========================================

    /// <summary>
    /// Permanent kobling til brukeren i AspNetUsers.
    ///
    /// Nullable fordi eldre behov kan være
    /// registrert før OwnerUserId ble innført.
    ///
    /// Nye behov skal få OwnerUserId fra
    /// den innloggede Identity-brukeren.
    /// </summary>
    public string? OwnerUserId { get; set; }


    /// <summary>
    /// Navigasjon til brukeren som eier behovet.
    ///
    /// ForeignKey forteller Entity Framework Core
    /// hvilken egenskap som brukes som nøkkel.
    /// </summary>
    [ForeignKey(nameof(OwnerUserId))]
    public ApplicationUser? OwnerUser { get; set; }


    // ========================================
    // BAKOVERKOMPATIBILITET
    // ========================================

    /// <summary>
    /// E-postadressen som ble brukt da behovet
    /// ble registrert.
    ///
    /// Beholdes foreløpig for eksisterende behov.
    /// Permanent eierskap skal etter hvert
    /// kontrolleres gjennom OwnerUserId.
    /// </summary>
    [StringLength(150)]
    public string OwnerEmail { get; init; } = string.Empty;


    // ========================================
    // METADATA
    // ========================================

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
