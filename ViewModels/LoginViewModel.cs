/*
    LoginViewModel.cs – innloggingsskjemaet

    Hva filen gjør: E-post, passord og «Husk meg».

    Oppgavekrav: 9, 18
*/

using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.ViewModels;

// Representerer data som brukes ved innlogging.
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

    // --- Husk meg ---

    /*
        Angir om brukeren ønsker å forbli
        innlogget etter at nettleseren lukkes.
    */
    [Display(Name = "Husk meg")]
    public bool RememberMe { get; set; } = false;
}
