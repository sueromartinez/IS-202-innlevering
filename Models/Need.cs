
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
    // FRIST OG KONTAKTPUNKT
    // ========================================

    /// <summary>
    /// Når behovet må være dekket (valgfritt). Lagres som dato.
    /// </summary>
    public DateTime? Deadline { get; set; }

    /// <summary>
    /// Hvem ressurseierne skal kontakte om behovet, f.eks. «Vakthavende, Grimstad kommune».
    /// </summary>
    [StringLength(100)]
    public string? ContactName { get; set; }

    [StringLength(30)]
    public string? ContactPhone { get; set; }


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


    // ========================================
    // STATUS
    // ========================================

    /// <summary>
    /// Hvor behovet er i statusflyten:
    /// Ny → Vurderes → Tildelt → Løst.
    /// </summary>
    public NeedStatus Status { get; set; } = NeedStatus.New;
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


/// <summary>
/// Statusflyten for et behov, slik caset beskriver den.
/// </summary>
public enum NeedStatus
{
    [Display(Name = "Ny")]
    New,

    /// <summary>
    /// Minst én ressurs er spurt, venter på svar.
    /// </summary>
    [Display(Name = "Vurderes")]
    UnderReview,

    /// <summary>
    /// En frivillig har bekreftet at ressursen stiller.
    /// </summary>
    [Display(Name = "Tildelt")]
    Assigned,

    [Display(Name = "Løst")]
    Resolved
}
