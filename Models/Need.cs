/*
    Need.cs – et behov

    Hva filen gjør: Beskriver et behov en offentlig aktør har registrert, for eksempel "3
    aggregater i Vennesla, akutt". Ligner Resource med vilje (type, antall, plassering), slik at
    et behov kan sammenlignes med ressurser av samme type i nærheten.

    På nettsiden: Det som fylles ut i «Registrer behov», og som vises i behovsoversikten
    («Behov» i menyen) og på Koordinering.

    Oppgavekrav: 10, 21, 22
*/

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FirstWebAppInDocker.Models;

/*
    Representerer et behov registrert av en offentlig aktør.
    Behovet kan matches med tilgjengelige ressurser basert
    på ressurstype, plassering og andre kriterier.

    Eierskap håndteres gjennom ASP.NET Core Identity.
    Eldre behov kan fremdeles være knyttet til e-postadresse.
*/
public class Need
{
    #region Identifikasjon

    // Unik identifikator for behovet.
    public int Id { get; init; }

    #endregion

    #region Behovsinformasjon

    // Ressurstypen som kreves for å dekke behovet.
    public ResourceType ResourceType { get; set; }
        = ResourceType.Other;

    // Akutt eller Planlagt. På nettsiden vises akutte behov først og med rød merkelapp.
    public NeedPriority Priority { get; set; }
        = NeedPriority.Acute;

    // Antall ressurser som etterspørres.
    public int Quantity { get; set; } = 1;

    // Beskrivelse av behovet og hva ressursen skal brukes til.
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    #endregion

    #region Geografisk plassering

    /*
        Geografisk breddegrad for behovet.

        Beholdes som databasefelt for kompatibilitet med
        eksisterende migrasjoner og EF Core-spørringer.
    */
    public double Latitude { get; set; }

    /*
        Geografisk lengdegrad for behovet.

        Beholdes som databasefelt for kompatibilitet med
        eksisterende migrasjoner og EF Core-spørringer.
    */
    public double Longitude { get; set; }

    /*
        Samlet geografisk posisjon representert som Coordinate.

        Egenskapen er ikke kartlagt til en egen databasekolonne.
        Den leser og oppdaterer de eksisterende koordinatfeltene.

        Bruk Latitude og Longitude i databaseoversatte
        LINQ-spørringer inntil geografisk databasemapping
        er implementert.
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

    #region Frist og kontaktinformasjon

    /*
        Datoen behovet senest bør være dekket.
        Null betyr at ingen frist er registrert.
    */
    public DateTime? Deadline { get; set; }

    // Navnet på kontaktpersonen for behovet.
    [StringLength(100)]
    public string? ContactName { get; set; }

    // Telefonnummeret til kontaktpersonen.
    [StringLength(30)]
    public string? ContactPhone { get; set; }

    #endregion

    #region Eierskap

    /*
        Hvilken offentlig aktør som eier behovet. Bare eieren (eller admin) kan endre eller
        slette det.
    */
    public string? OwnerUserId { get; set; }

    // Navigasjon til Identity-brukeren som eier behovet.
    [ForeignKey(nameof(OwnerUserId))]
    public ApplicationUser? OwnerUser { get; set; }

    /*
        E-postadressen som ble registrert som behovets eier.

        Beholdes for bakoverkompatibilitet med eldre behov.
        Nye eierskapskontroller skal bruke OwnerUserId.
    */
    [StringLength(150)]
    public string OwnerEmail { get; init; } = string.Empty;

    #endregion

    #region Metadata og status

    // Tidspunktet behovet ble registrert, angitt i UTC.
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /*
        Gangen i et behov: Nytt → Til vurdering → Tildelt → Løst. Vises som kolonnene på
        Koordinering-siden.
    */
    public NeedStatus Status { get; set; } = NeedStatus.New;

    #endregion
}

/*
    [Display(Name = ...)] gir norske navn på nettsiden, mens koden bruker engelske navn.
*/
public enum NeedPriority
{
    // Behov som krever rask oppfølging.
    [Display(Name = "Akutt")]
    Acute = 0,

    // Behov som kan planlegges på forhånd.
    [Display(Name = "Planlagt")]
    Planned = 1
}

/*
    Representerer statusflyten for et registrert behov.

    Eksisterende tallverdier beholdes fordi statusene
    allerede kan være lagret som heltall i databasen.
*/
public enum NeedStatus
{
    // Behovet er opprettet.
    [Display(Name = "Ny")]
    New = 0,

    // Minst én ressurs er forespurt, og behovet vurderes.
    [Display(Name = "Vurderes")]
    UnderReview = 1,

    // En ressurs har bekreftet en tildeling.
    [Display(Name = "Tildelt")]
    Assigned = 2,

    // Behovet er ferdig behandlet og løst.
    [Display(Name = "Løst")]
    Resolved = 3
}
