
using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Data som brukes når en ny ressursleverandør
/// registrerer bruker i Beredskapsportalen.
/// </summary>
public class RegisterViewModel
{
    // ========================================
    // NAVN
    // ========================================

    [Required(ErrorMessage = "Navn er påkrevd.")]
    [StringLength(
        100,
        ErrorMessage = "Navnet kan maksimalt være 100 tegn."
    )]
    public string Name { get; set; } = string.Empty;


    // ========================================
    // E-POST
    // ========================================

    [Required(ErrorMessage = "E-post er påkrevd.")]
    [EmailAddress(
        ErrorMessage = "Skriv inn en gyldig e-postadresse."
    )]
    public string Email { get; set; } = string.Empty;


    // ========================================
    // LANDSKODE
    // ========================================

    [Required(ErrorMessage = "Velg landskode.")]
    [RegularExpression(
        @"^\+(47|46|45)$",
        ErrorMessage = "Velg en gyldig landskode."
    )]
    [Display(Name = "Landskode")]
    public string PhoneCountryCode { get; set; } = "+47";


    // ========================================
    // TELEFONNUMMER
    // ========================================

    // Telefonnummer er valgfritt.
    // Brukeren kan skrive nummeret
    // med eller uten mellomrom.

    [Display(Name = "Telefonnummer")]
    [StringLength(
        20,
        ErrorMessage = "Telefonnummeret er for langt."
    )]
    [RegularExpression(
        @"^[0-9 ]{5,20}$",
        ErrorMessage =
            "Telefonnummeret må inneholde " +
            "5–20 tegn og bare tall eller mellomrom."
    )]
    [DataType(DataType.PhoneNumber)]
    public string? PhoneNumber { get; set; }


    // ========================================
    // PASSORD
    // ========================================

    [Required(ErrorMessage = "Passord er påkrevd.")]
    [DataType(DataType.Password)]
    [StringLength(
        100,
        MinimumLength = 8,
        ErrorMessage = "Passordet må være minst 8 tegn."
    )]
    public string Password { get; set; } = string.Empty;


    // ========================================
    // BEKREFT PASSORD
    // ========================================

    [Required(ErrorMessage = "Bekreft passordet.")]
    [DataType(DataType.Password)]
    [Compare(
        nameof(Password),
        ErrorMessage = "Passordene er ikke like."
    )]
    public string ConfirmPassword { get; set; } = string.Empty;
}
