/*
    RegisterNeedsViewModel.cs – skjemaet «Registrer behov»

    Hva filen gjør: En liste med behov (NeedItemViewModel), så flere behov kan registreres i ett
    skjema. NeedItemViewModel brukes også når ett behov endres.

    Oppgavekrav: 18, 21
*/

using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/*
    Skjemaet for å registrere behov.
    Inneholder en liste, slik at flere behov kan sendes inn samtidig.
*/
public class RegisterNeedsViewModel
{
    public List<NeedItemViewModel> Needs { get; init; } = new();
}

/*
    Ett behov i skjemaet.
    Brukes både ved registrering og ved endring.
*/
public class NeedItemViewModel
{
    // Id til behovet som endres. Er 0 ved registrering.
    public int Id { get; init; }

    [Required(ErrorMessage = "Velg type behov")]
    [Display(Name = "Type behov")]
    public ResourceType? ResourceType { get; init; }

    [Display(Name = "Prioritet")]
    public NeedPriority Priority { get; init; } = NeedPriority.Acute;

    [Range(1, 9999, ErrorMessage = "Antall må være mellom 1 og 9999")]
    [Display(Name = "Antall")]
    public int Quantity { get; init; } = 1;

    [Required(ErrorMessage = "Beskrivelse er påkrevd")]
    [StringLength(500, ErrorMessage = "Maks 500 tegn")]
    [Display(Name = "Beskrivelse")]
    public string Description { get; init; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Frist")]
    public DateTime? Deadline { get; init; }

    [StringLength(100, ErrorMessage = "Maks 100 tegn")]
    [Display(Name = "Kontaktperson")]
    public string? ContactName { get; init; }

    [StringLength(30, ErrorMessage = "Maks 30 tegn")]
    [Phone(ErrorMessage = "Skriv inn et gyldig telefonnummer")]
    [Display(Name = "Telefon")]
    public string? ContactPhone { get; init; }

    // Settes når brukeren klikker i kartet
    public double? Latitude { get; init; }

    public double? Longitude { get; init; }
}
