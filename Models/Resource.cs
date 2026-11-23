using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Representerer en ressurs registrert av en ressursleverandør
/// i Beredskapsportalen.
/// </summary>
/// <remarks>
/// Ressursen inneholder informasjon om type, kapasitet,
/// tilgjengelighet, geografisk plassering og eierskap.
/// </remarks>
public class Resource
{
    #region Identifikasjon

    /// <summary>
    /// Unik identifikator for ressursen.
    /// </summary>
    public int Id { get; set; }

    [Required]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    #endregion

    #region Ressursinformasjon

    /// <summary>
    /// Kategorien ressursen tilhører.
    /// </summary>
    [EnumDataType(typeof(ResourceType),
        ErrorMessage = "Velg en gyldig ressurskategori.")]
    public ResourceType ResourceType { get; set; }
        = ResourceType.Other;

    /// <summary>
    /// Navnet på ressursen.
    /// </summary>
    [Required(ErrorMessage = "Du må oppgi et ressursnavn.")]
    [StringLength(100, MinimumLength = 3,
        ErrorMessage = "Ressursnavnet må være mellom 3 og 100 tegn.")]
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// Beskrivelse av ressursens egenskaper og bruksområder.
    /// </summary>
    [Required(ErrorMessage = "Du må skrive en beskrivelse.")]
    [StringLength(500, MinimumLength = 10,
        ErrorMessage = "Beskrivelsen må være mellom 10 og 500 tegn.")]
    public string Description { get; set; } = string.Empty;

     /// <summary>
    /// Antall enheter som er registrert.
    /// </summary>
    [Range(1, int.MaxValue,
        ErrorMessage = "Antallet må være minst 1.")]
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Ressursens kapasitet, beskrevet ut fra ressurstype.
    /// </summary>
    [StringLength(200)]
    public string? Capacity { get; set; }

    /// <summary>
    /// Beskrivelse av når ressursen er tilgjengelig.
    /// </summary>
    [StringLength(200)]
    public string? Availability { get; set; }

    #endregion

    #region Geografisk plassering

    /// <summary>
    /// Ressursens registrerte adresse.
    /// </summary>
    [StringLength(200)]
    public string? Address { get; set; }

    /// <summary>
    /// Geografisk breddegrad for ressursen.
    /// </summary>
    /// <remarks>
    /// Eksisterende databasefelt beholdes for kompatibilitet
    /// med EF Core, kartvisninger og matching.
    /// </remarks>
    public double Latitude { get; set; }

    /// <summary>
    /// Geografisk lengdegrad for ressursen.
    /// </summary>
    /// <remarks>
    /// Eksisterende databasefelt beholdes for kompatibilitet
    /// med EF Core, kartvisninger og matching.
    /// </remarks>
    public double Longitude { get; set; }

    /// <summary>
    /// Ressursens geografiske posisjon som Coordinate.
    /// </summary>
    /// <remarks>
    /// Egenskapen bruker de eksisterende koordinatfeltene
    /// og oppretter ingen ekstra databasekolonner.
    ///
    /// Bruk de kartlagte koordinatfeltene i EF Core-spørringer
    /// inntil datamodellen støtter geografiske databasetyper.
    /// </remarks>
    [NotMapped]
    public Coordinate Position
    {
        get => new Coordinate(Latitude, Longitude);

        set
        {
            Latitude = value.Latitude;
            Longitude = value.Longitude;
        }
    }

    #endregion

    #region Kontaktinformasjon

    /// <summary>
    /// E-postadressen til ressursens kontaktperson.
    /// </summary>
    [EmailAddress]
    [StringLength(150)]
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Landskoden til kontaktpersonens telefonnummer.
    /// </summary>
    [StringLength(5)]
    public string? ContactPhoneCountryCode { get; set; }

    /// <summary>
    /// Kontaktpersonens telefonnummer uten landskode.
    /// </summary>
    [StringLength(20)]
    public string? ContactPhoneNumber { get; set; }

    #endregion

    #region Eierskap

    /// <summary>
    /// Permanent Identity-ID til brukeren
    /// som eier ressursen.
    ///
    /// Nullable forelÃ¸pig fordi eldre ressurser
    /// kan vÃ¦re registrert fÃ¸r Identity ble innfÃ¸rt.
    /// </summary>
    /// <remarks>
    /// Nullable fordi eldre ressurser kan mangle Identity-ID.
    /// </remarks>
    [StringLength(255)]
    public string? OwnerUserId { get; set; }

    /// <summary>
    /// Navigasjon til Identity-brukeren som eier ressursen.
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
    /// <remarks>
    /// Beholdes av hensyn til eldre data og historikk.
    /// Permanent eierskap skal kontrolleres gjennom OwnerUserId.
    /// </remarks>
    [Required]
    [StringLength(150)]
    public string OwnerEmail { get; set; } = string.Empty;

    #endregion

    #region Metadata og status

    /// <summary>
    /// Tidspunktet ressursen ble registrert, angitt i UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Angir om ressursen er tilgjengelig.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Ressursens tilgjengelighetsstatus.
    /// </summary>
    /// <remarks>
    /// Ressursen blir opptatt ved bekreftet tildeling
    /// og kan bli tilgjengelig igjen når tildelingen avsluttes.
    /// </remarks>
    public ResourceStatus Status { get; set; }
        = ResourceStatus.Available;

    #endregion
}

/// <summary>
/// Angir om en ressurs er tilgjengelig for forespørsler.
/// </summary>
/// <remarks>
/// Tallverdiene beholdes av hensyn til eksisterende data.
/// </remarks>
public enum ResourceStatus
{
    /// <summary>Ressursen er tilgjengelig.</summary>
    [Display(Name = "Tilgjengelig")]
    Available = 0,

    /// <summary>Ressursen er opptatt av en tildeling.</summary>
    [Display(Name = "Opptatt")]
    Busy = 1,

    /// <summary>Ressursen er ikke tilgjengelig.</summary>
    [Display(Name = "Utilgjengelig")]
    Unavailable = 2
}