using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Representerer en ressurs som en ressursleverandør
/// har registrert i Beredskapsportalen.
/// </summary>
public class Resource
{
    public int Id { get; set; }


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
    /// Nullable foreløpig fordi eldre ressurser
    /// kan være registrert før Identity ble innført.
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
    /// Beholdes foreløpig for eldre data
    /// og kontakt-/historikkformål.
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

    public DateTime CreatedAt { get; set; }
        = DateTime.UtcNow;
}