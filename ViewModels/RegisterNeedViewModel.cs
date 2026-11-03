using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Representerer data en offentlig aktør fyller inn
/// når et nytt behov registreres.
/// </summary>
public class RegisterNeedViewModel
{
    /// <summary>
    /// Kort tittel på behovet, for eksempel "Strømbehov".
    /// </summary>
    [Required(ErrorMessage = "Tittel er påkrevd")]
    [StringLength(100, ErrorMessage = "Tittelen kan maks være 100 tegn")]
    [Display(Name = "Tittel")]
    public string Title { get; init; } = string.Empty;


    /// <summary>
    /// Hvilken type ressurs som trengs.
    /// Nullable slik at "Velg type" gir valideringsfeil.
    /// </summary>
    [Required(ErrorMessage = "Du må velge type ressurs")]
    [Display(Name = "Type ressurs")]
    public ResourceType? ResourceType { get; init; }


    /// <summary>
    /// Beskrivelse av behovet.
    /// </summary>
    [Required(ErrorMessage = "Beskrivelse er påkrevd")]
    [StringLength(500, ErrorMessage = "Beskrivelsen kan maks være 500 tegn")]
    [Display(Name = "Beskrivelse")]
    public string Description { get; init; } = string.Empty;


    /// <summary>
    /// Akutt eller planlagt. Nullable av samme grunn som ResourceType.
    /// </summary>
    [Required(ErrorMessage = "Du må velge prioritet")]
    [Display(Name = "Prioritet")]
    public NeedPriority? Priority { get; init; }


    /// <summary>
    /// Breddegrad valgt i kartet.
    /// </summary>
    public double Latitude { get; init; }


    /// <summary>
    /// Lengdegrad valgt i kartet.
    /// </summary>
    public double Longitude { get; init; }
}