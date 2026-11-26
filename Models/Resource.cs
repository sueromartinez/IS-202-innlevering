/*
    Resource.cs – en ressurs

    Hva filen gjør: Beskriver én ressurs en frivillig har registrert, for eksempel et aggregat,
    en båt eller en traktor. Blir tabellen Resources i databasen. Attributtene ([Required],
    [StringLength] ...) gir riktige kolonner i databasen og automatisk sjekk av skjemaet.

    På nettsiden: Det brukeren fyller ut i «Registrer ressurs» havner her. Det vises igjen i
    ressursoversikten («Ressurser» i menyen), på dashbordet og som markør på kartet.

    Oppgavekrav: 9, 13, 21, 22
*/

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FirstWebAppInDocker.Models;

/*
    Representerer en ressurs registrert av en ressursleverandør
    i Beredskapsportalen.

    Ressursen inneholder informasjon om type, kapasitet,
    tilgjengelighet, geografisk plassering og eierskap.
*/
public class Resource
{
    #region Identifikasjon

    // Unik identifikator for ressursen.
    public int Id { get; set; }

    #endregion

    #region Ressursinformasjon

    /*
        Kategorien (Transport, Drone, Aggregat ...) er en fast liste (enum). Da kan matchingen
        sammenligne type mot type i stedet for fritekst.
        På nettsiden: nedtrekkslisten «Type» i skjemaet og ikonet ved hver ressurs.
    */
    [Required]
    public ResourceType ResourceType { get; set; }
        = ResourceType.Other;

    // Navnet på ressursen.
    [Required]
    [StringLength(100)]
    public string ResourceName { get; set; } = string.Empty;

    // Beskrivelse av ressursens egenskaper og bruksområder.
    [Required]
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    // Antall enheter som er registrert.
    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;

    // Ressursens kapasitet, beskrevet ut fra ressurstype.
    [StringLength(200)]
    public string? Capacity { get; set; }

    // Beskrivelse av når ressursen er tilgjengelig.
    [StringLength(200)]
    public string? Availability { get; set; }

    #endregion

    #region Geografisk plassering

    // Ressursens registrerte adresse.
    [StringLength(200)]
    public string? Address { get; set; }

    /*
        Plasseringen fra kartet i skjemaet (breddegrad/lengdegrad).
        På nettsiden: brukes til markøren på kartene og til å regne ut avstand til behov.
    */
    public double Latitude { get; set; }

    /*
        Geografisk lengdegrad for ressursen.

        Eksisterende databasefelt beholdes for kompatibilitet
        med EF Core, kartvisninger og matching.
    */
    public double Longitude { get; set; }

    /*
        [NotMapped] betyr at dette ikke er en kolonne i databasen, bare en hjelpeegenskap i
        koden.

        Ressursens geografiske posisjon som Coordinate.

        Egenskapen bruker de eksisterende koordinatfeltene
        og oppretter ingen ekstra databasekolonner.

        Bruk de kartlagte koordinatfeltene i EF Core-spørringer
        inntil datamodellen støtter geografiske databasetyper.
    */
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

    // E-postadressen til ressursens kontaktperson.
    [EmailAddress]
    [StringLength(150)]
    public string? ContactEmail { get; set; }

    // Landskoden til kontaktpersonens telefonnummer.
    [StringLength(5)]
    public string? ContactPhoneCountryCode { get; set; }

    // Kontaktpersonens telefonnummer uten landskode.
    [StringLength(20)]
    public string? ContactPhoneNumber { get; set; }

    #endregion

    #region Eierskap

    /*
        Hvem som eier ressursen. Brukes til at en frivillig bare ser og kan endre sine egne
        ressurser.
    */
    [StringLength(255)]
    public string? OwnerUserId { get; set; }

    // Navigasjon til Identity-brukeren som eier ressursen.
    [ForeignKey(nameof(OwnerUserId))]
    public ApplicationUser? Owner { get; set; }

    /*
        E-postadressen som ble registrert som ressurseier.

        Beholdes av hensyn til eldre data og historikk.
        Permanent eierskap skal kontrolleres gjennom OwnerUserId.
    */
    [Required]
    [StringLength(150)]
    public string OwnerEmail { get; set; } = string.Empty;

    #endregion

    #region Metadata og status

    // Tidspunktet ressursen ble registrert, angitt i UTC.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /*
        Tilgjengelig, Opptatt eller Utilgjengelig.
        På nettsiden: den fargede statusmerkelappen. Bare tilgjengelige ressurser foreslås i
        matchingen.
    */
    public ResourceStatus Status { get; set; }
        = ResourceStatus.Available;

    #endregion
}

/*
    Angir om en ressurs er tilgjengelig for forespørsler.

    Tallverdiene beholdes av hensyn til eksisterende data.
*/
public enum ResourceStatus
{
    // Ressursen er tilgjengelig.
    [Display(Name = "Tilgjengelig")]
    Available = 0,

    // Ressursen er opptatt av en tildeling.
    [Display(Name = "Opptatt")]
    Busy = 1,

    // Ressursen er ikke tilgjengelig.
    [Display(Name = "Utilgjengelig")]
    Unavailable = 2
}
