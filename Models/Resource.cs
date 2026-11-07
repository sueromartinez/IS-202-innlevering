using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Representerer en ressurs som en ressursleverandÃ¸r
/// har registrert i Beredskapsportalen.
/// </summary>
public class Resource
{
    public int Id { get; set; }

    [Required]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;


    // =========================
    // RESOURCE INFORMATION
    // =========================

    [Required]
    public ResourceType ResourceType { get; set; }


    [Required]
    [StringLength(100)]
    public string ResourceName { get; set; }
        = string.Empty;


    [Required]
    [StringLength(500)]
    public string Description { get; set; }
        = string.Empty;


    [Range(
        1,
        int.MaxValue
    )]
    public int Quantity { get; set; }
        = 1;


    [StringLength(200)]
    public string? Capacity { get; set; }


    [StringLength(200)]
    public string? Availability { get; set; }


    // =========================
    // LOCATION
    // =========================

    [StringLength(200)]
    public string? Address { get; set; }


    public double Latitude { get; set; }

    public double Longitude { get; set; }


    // =========================
    // CONTACT INFORMATION
    // =========================

    [EmailAddress]
    [StringLength(150)]
    public string? ContactEmail { get; set; }


    [StringLength(5)]
    public string? ContactPhoneCountryCode { get; set; }


    [StringLength(20)]
    public string? ContactPhoneNumber { get; set; }


    // =========================
    // OWNERSHIP
    // =========================

    /// <summary>
    /// Permanent Identity-ID til brukeren
    /// som eier ressursen.
    ///
    /// Nullable forelÃ¸pig fordi eldre ressurser
    /// kan vÃ¦re registrert fÃ¸r Identity ble innfÃ¸rt.
    /// </summary>
    [StringLength(255)]
    public string? OwnerUserId { get; set; }


    /// <summary>
    /// Navigasjon til brukeren i AspNetUsers.
    /// OwnerUserId brukes som foreign key.
    /// </summary>
    [ForeignKey(nameof(OwnerUserId))]
    public ApplicationUser? Owner { get; set; }


    /// <summary>
    /// Beholdes forelÃ¸pig for eldre data
    /// og kontakt-/historikkformÃ¥l.
    ///
    /// Selve eierskapet skal styres
    /// gjennom OwnerUserId.
    /// </summary>
    [Required]
    [StringLength(150)]
    public string OwnerEmail { get; set; }
        = string.Empty;


    // =========================
    // METADATA
    // =========================

    // =========================
    // METADATA
    // =========================

    public DateTime CreatedAt { get; set; }
        = DateTime.UtcNow;

    /// <summary>
    /// Angir om ressursen er tilgjengelig.
    /// </summary>
    public bool IsAvailable { get; set; } = true;


    // =========================
    // STATUS
    // =========================

    /// <summary>
    /// Om ressursen kan spørres om hjelp nå.
    /// Settes til Busy når en frivillig bekrefter
    /// en tildeling, og tilbake når behovet er løst.
    /// </summary>
    public ResourceStatus Status { get; set; }
        = ResourceStatus.Available;
}


/// <summary>
/// Tilgjengelighet for en ressurs.
/// </summary>
public enum ResourceStatus
{
    [Display(Name = "Tilgjengelig")]
    Available,

    [Display(Name = "Opptatt")]
    Busy,

    [Display(Name = "Utilgjengelig")]
    Unavailable
}
