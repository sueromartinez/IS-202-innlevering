/*
    TwoFactorViewModels.cs

    Hva filen gjør: Skjemaet der man skriver inn den 6-sifrede tofaktorkoden.

    Oppgavekrav: 18
*/

using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.ViewModels;

// --- Tofaktor ved innlogging ---

public class TwoFactorLoginViewModel
{
    [Required(ErrorMessage = "Skriv inn engangskoden.")]
    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "Koden må inneholde seks sifre.")]
    public string Code { get; set; } = string.Empty;
}
