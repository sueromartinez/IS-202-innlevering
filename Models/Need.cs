
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Representerer et behov registrert av en offentlig aktør.
/// Behovet kan matches med tilgjengelige ressurser basert
/// på ressurstype, plassering og andre kriterier.
/// </summary>
/// <remarks>
/// Eierskap håndteres gjennom ASP.NET Core Identity.
/// Eldre behov kan fremdeles være knyttet til e-postadresse.
/// </remarks>
public class Need
{
    #region Identifikasjon

    /// <summary>
    /// Unik identifikator for behovet.
    /// </summary>
    public int Id { get; init; }

    #endregion

    #region Behovsinformasjon

    /// <summary>
    /// Ressurstypen som kreves for å dekke behovet.
    /// </summary>
    public ResourceType ResourceType { get; set; }
        = ResourceType.Other;

    /// <summary>
    /// Angir om behovet er akutt eller planlagt.
    /// </summary>
    public NeedPriority Priority { get; set; }
        = NeedPriority.Acute;

    /// <summary>
    /// Antall ressurser som etterspørres.
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Beskrivelse av behovet og hva ressursen skal brukes til.
    /// </summary>
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    #endregion

    #region Geografisk plassering

    /// <summary>
    /// Geografisk breddegrad for behovet.
    /// </summary>
    /// <remarks>
    /// Beholdes som databasefelt for kompatibilitet med
    /// eksisterende migrasjoner og EF Core-spørringer.
    /// </remarks>
    public double Latitude { get; set; }

    /// <summary>
    /// Geografisk lengdegrad for behovet.
    /// </summary>
    /// <remarks>
    /// Beholdes som databasefelt for kompatibilitet med
    /// eksisterende migrasjoner og EF Core-spørringer.
    /// </remarks>
    public double Longitude { get; set; }

    /// <summary>
    /// Samlet geografisk posisjon representert som Coordinate.
    /// </summary>
    /// <remarks>
    /// Egenskapen er ikke kartlagt til en egen databasekolonne.
    /// Den leser og oppdaterer de eksisterende koordinatfeltene.
    ///
    /// Bruk Latitude og Longitude i databaseoversatte
    /// LINQ-spørringer inntil geografisk databasemapping
    /// er implementert.
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

    #region Frist og kontaktinformasjon

    /// <summary>
    /// Datoen behovet senest bør være dekket.
    /// Null betyr at ingen frist er registrert.
    /// </summary>
    public DateTime? Deadline { get; set; }

    /// <summary>
    /// Navnet på kontaktpersonen for behovet.
    /// </summary>
    [StringLength(100)]
    public string? ContactName { get; set; }

    /// <summary>
    /// Telefonnummeret til kontaktpersonen.
    /// </summary>
    [StringLength(30)]
    public string? ContactPhone { get; set; }

    #endregion

    #region Eierskap

    /// <summary>
    /// Permanent Identity-ID til brukeren som eier behovet.
    /// </summary>
    /// <remarks>
    /// Nullable for å støtte eldre behov som ble registrert
    /// før permanent eierskap gjennom Identity ble innført.
    /// </remarks>
    public string? OwnerUserId { get; set; }

    /// <summary>
    /// Navigasjon til Identity-brukeren som eier behovet.
    /// </summary>
    [ForeignKey(nameof(OwnerUserId))]
    public ApplicationUser? OwnerUser { get; set; }

    /// <summary>
    /// E-postadressen som ble registrert som behovets eier.
    /// </summary>
    /// <remarks>
    /// Beholdes for bakoverkompatibilitet med eldre behov.
    /// Nye eierskapskontroller skal bruke OwnerUserId.
    /// </remarks>
    [StringLength(150)]
    public string OwnerEmail { get; init; } = string.Empty;

    #endregion

    #region Metadata og status

    /// <summary>
    /// Tidspunktet behovet ble registrert, angitt i UTC.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gjeldende status i behovets behandlingsflyt.
    /// </summary>
    public NeedStatus Status { get; set; } = NeedStatus.New;

    #endregion
}

/// <summary>
/// Angir hvor raskt et behov må dekkes.
/// </summary>
/// <remarks>
/// Tallverdiene er eksplisitte for å bevare kompatibilitet
/// med eksisterende databaseoppføringer.
/// </remarks>
public enum NeedPriority
{
    /// <summary>Behov som krever rask oppfølging.</summary>
    [Display(Name = "Akutt")]
    Acute = 0,

    /// <summary>Behov som kan planlegges på forhånd.</summary>
    [Display(Name = "Planlagt")]
    Planned = 1
}

/// <summary>
/// Representerer statusflyten for et registrert behov.
/// </summary>
/// <remarks>
/// Eksisterende tallverdier beholdes fordi statusene
/// allerede kan være lagret som heltall i databasen.
/// </remarks>
public enum NeedStatus
{
    /// <summary>Behovet er opprettet.</summary>
    [Display(Name = "Ny")]
    New = 0,

    /// <summary>
    /// Minst én ressurs er forespurt, og behovet vurderes.
    /// </summary>
    [Display(Name = "Vurderes")]
    UnderReview = 1,

    /// <summary>
    /// En ressurs har bekreftet en tildeling.
    /// </summary>
    [Display(Name = "Tildelt")]
    Assigned = 2,

    /// <summary>
    /// Behovet er ferdig behandlet og løst.
    /// </summary>
    [Display(Name = "Løst")]
    Resolved = 3
}
