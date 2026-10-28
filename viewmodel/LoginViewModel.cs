using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Representerer data som brukes ved innlogging.
/// </summary>
public class LoginViewModel
{
    [Required(ErrorMessage = "E-post er påkrevd")]
    [EmailAddress(ErrorMessage = "Skriv inn en gyldig e-postadresse")]
    [Display(Name = "E-post")]
    public string Email { get; init; } = string.Empty;


    [Required(ErrorMessage = "Passord er påkrevd")]
    [DataType(DataType.Password)]
    [Display(Name = "Passord")]
    public string Password { get; init; } = string.Empty;
}