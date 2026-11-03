using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Data som brukes når en ny ressursleverandør
/// registrerer bruker i Beredskapsportalen.
/// </summary>
public class RegisterViewModel
{
    [Required(ErrorMessage = "Navn er påkrevd.")]
    [StringLength(
        100,
        ErrorMessage = "Navnet kan maksimalt være 100 tegn."
    )]
    public string Name { get; set; } = string.Empty;


    [Required(ErrorMessage = "E-post er påkrevd.")]
    [EmailAddress(
        ErrorMessage = "Skriv inn en gyldig e-postadresse."
    )]
    public string Email { get; set; } = string.Empty;


    [Required(ErrorMessage = "Passord er påkrevd.")]
    [DataType(DataType.Password)]
    [StringLength(
        100,
        MinimumLength = 8,
        ErrorMessage = "Passordet må være minst 8 tegn."
    )]
    public string Password { get; set; } = string.Empty;


    [Required(ErrorMessage = "Bekreft passordet.")]
    [DataType(DataType.Password)]
    [Compare(
        nameof(Password),
        ErrorMessage = "Passordene er ikke like."
    )]
    public string ConfirmPassword { get; set; } = string.Empty;
}