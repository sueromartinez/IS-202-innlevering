using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Representerer en ressurs som vises
/// i kart og ressursoversikt.
/// </summary>
public class PositionModel
{
    [Required(
        ErrorMessage = "Du må velge type ressurs"
    )]
    [Display(Name = "Type ressurs")]
    public ResourceType? ResourceType { get; init; }


    [Required(
        ErrorMessage = "Beskrivelse er påkrevd"
    )]
    [StringLength(
        500,
        ErrorMessage = "Beskrivelsen kan maks være 500 tegn"
    )]
    [Display(Name = "Beskrivelse")]
    public string Description { get; init; } = string.Empty;


    /// <summary>
    /// Geografisk plassering av ressursen.
    /// </summary>
    public Coordinate Position { get; init; }


    [EmailAddress(
        ErrorMessage = "Skriv inn en gyldig e-postadresse"
    )]
    [StringLength(
        150,
        ErrorMessage = "E-postadressen er for lang"
    )]
    [Display(Name = "E-post")]
    public string? ContactEmail { get; init; }


    [StringLength(5)]
    [Display(Name = "Landskode")]
    public string? ContactPhoneCountryCode { get; init; }


    [StringLength(
        20,
        ErrorMessage = "Telefonnummeret er for langt"
    )]
    [Display(Name = "Telefonnummer")]
    public string? ContactPhoneNumber { get; init; }


    /// <summary>
    /// Midlertidig identifikasjon av eieren.
    /// Skal senere erstattes av ASP.NET Identity UserId.
    /// </summary>
    [StringLength(150)]
    public string OwnerEmail { get; init; } = string.Empty;
}