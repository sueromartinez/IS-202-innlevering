using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Skjemaet for å registrere behov.
/// Inneholder en liste, slik at flere behov kan sendes inn samtidig.
/// </summary>
public class RegisterNeedsViewModel
{
    public List<NeedItemViewModel> Needs { get; init; } = new();
}


/// <summary>
/// Ett behov i skjemaet.
/// Brukes både ved registrering og ved endring.
/// </summary>
public class NeedItemViewModel
{
    /// <summary>
    /// Id til behovet som endres. Er 0 ved registrering.
    /// </summary>
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

    // Settes når brukeren klikker i kartet
    public double? Latitude { get; init; }

    public double? Longitude { get; init; }
}