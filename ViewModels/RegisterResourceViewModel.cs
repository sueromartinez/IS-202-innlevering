using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Data som brukes når en ressursleverandør
/// registrerer en ressurs.
/// </summary>
public class RegisterResourceViewModel
{
    // =========================
    // RESOURCE INFORMATION
    // =========================

    [Required(
        ErrorMessage = "Velg type ressurs."
    )]
    [Display(
        Name = "Ressurstype"
    )]
    public ResourceType? ResourceType { get; set; }


    [Required(
        ErrorMessage = "Navn på ressursen er påkrevd."
    )]
    [StringLength(
        100,
        ErrorMessage =
            "Ressursnavnet kan maksimalt være 100 tegn."
    )]
    [Display(
        Name = "Navn på ressurs"
    )]
    public string ResourceName { get; set; }
        = string.Empty;


    [Required(
        ErrorMessage = "Beskrivelse er påkrevd."
    )]
    [StringLength(
        500,
        ErrorMessage =
            "Beskrivelsen kan maksimalt være 500 tegn."
    )]
    [Display(
        Name = "Beskrivelse"
    )]
    public string Description { get; set; }
        = string.Empty;


    [Range(
        1,
        100000,
        ErrorMessage =
            "Antall må være minst 1."
    )]
    [Display(
        Name = "Antall"
    )]
    public int Quantity { get; set; }
        = 1;


    [StringLength(
        200,
        ErrorMessage =
            "Kapasitet kan maksimalt være 200 tegn."
    )]
    [Display(
        Name = "Kapasitet"
    )]
    public string? Capacity { get; set; }


    [StringLength(
        200,
        ErrorMessage =
            "Tilgjengelighet kan maksimalt være 200 tegn."
    )]
    [Display(
        Name = "Tilgjengelighet"
    )]
    public string? Availability { get; set; }


    // =========================
    // LOCATION
    // =========================

    [StringLength(
        200,
        ErrorMessage =
            "Adresse kan maksimalt være 200 tegn."
    )]
    [Display(
        Name = "Adresse / område"
    )]
    public string? Address { get; set; }


    [Range(
        -90,
        90,
        ErrorMessage =
            "Ugyldig breddegrad."
    )]
    public double Latitude { get; set; }


    [Range(
        -180,
        180,
        ErrorMessage =
            "Ugyldig lengdegrad."
    )]
    public double Longitude { get; set; }


    // =========================
    // CONTACT INFORMATION
    // =========================

    [EmailAddress(
        ErrorMessage =
            "Skriv inn en gyldig e-postadresse."
    )]
    [StringLength(
        150
    )]
    [Display(
        Name = "Kontakt-e-post"
    )]
    public string? ContactEmail { get; set; }


    [StringLength(
        5
    )]
    [Display(
        Name = "Landkode"
    )]
    public string? ContactPhoneCountryCode { get; set; }
        = "+47";


    [StringLength(
        20,
        ErrorMessage =
            "Telefonnummeret kan maksimalt være 20 tegn."
    )]
    [Phone(
        ErrorMessage =
            "Skriv inn et gyldig telefonnummer."
    )]
    [Display(
        Name = "Telefonnummer"
    )]
    public string? ContactPhoneNumber { get; set; }
}