using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Representerer en ressursposisjon som brukes
/// mellom controller, view og kartvisning.
/// </summary>
public class PositionModel
{
    /// <summary>
    /// Type ressurs.
    /// </summary>
    [Required(
        ErrorMessage = "Du må velge type ressurs"
    )]
    [Display(
        Name = "Type ressurs"
    )]
    public ResourceType? ResourceType { get; init; }


    /// <summary>
    /// Beskrivelse av ressursen.
    /// </summary>
    [Required(
        ErrorMessage = "Beskrivelse er påkrevd"
    )]
    [StringLength(
        500,
        ErrorMessage = "Beskrivelsen kan maks være 500 tegn"
    )]
    [Display(
        Name = "Beskrivelse"
    )]
    public string Description { get; init; } = string.Empty;


    /// <summary>
    /// Geografisk posisjon.
    /// </summary>
    public Coordinate Position { get; init; }


    /*
     * Disse to beholdes midlertidig fordi eldre views
     * fortsatt bruker Latitude og Longitude direkte.
     */
    public double Latitude { get; init; }

    public double Longitude { get; init; }


    /// <summary>
    /// Kontakt-e-post.
    /// </summary>
    [EmailAddress(
        ErrorMessage = "Skriv inn en gyldig e-postadresse"
    )]
    [StringLength(
        150,
        ErrorMessage = "E-postadressen er for lang"
    )]
    [Display(
        Name = "E-post"
    )]
    public string? ContactEmail { get; init; }


    /// <summary>
    /// Landskode.
    /// </summary>
    [StringLength(5)]
    [Display(
        Name = "Landskode"
    )]
    public string? ContactPhoneCountryCode { get; init; }


    /// <summary>
    /// Telefonnummer.
    /// </summary>
    [StringLength(
        20,
        ErrorMessage = "Telefonnummeret er for langt"
    )]
    [Display(
        Name = "Telefonnummer"
    )]
    public string? ContactPhoneNumber { get; init; }


    /// <summary>
    /// Midlertidig støtte for eldre views.
    /// Senere fjerner vi denne og bruker
    /// separate kontaktfelt overalt.
    /// </summary>
    [StringLength(
        250,
        ErrorMessage = "Kontaktinformasjonen er for lang"
    )]
    public string? ContactInfo { get; init; }


    /// <summary>
    /// Midlertidig identifikasjon av eieren.
    /// Skal senere erstattes med Identity UserId.
    /// </summary>
    [StringLength(150)]
    public string OwnerEmail { get; init; } = string.Empty;
}