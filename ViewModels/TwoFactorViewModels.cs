
using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.ViewModels;

// ========================================
// SETUP AV TOFAKTORAUTENTISERING
// ========================================

public class TwoFactorSetupViewModel
{
    // Nøkkelen som brukeren kan legge inn
    // i en autentiseringsapp.
    public string SharedKey { get; set; } =
        string.Empty;

    // URI for å koble kontoen til
    // Microsoft Authenticator eller lignende.
    public string AuthenticatorUri { get; set; } =
        string.Empty;

    // Engangskoden brukes for å bekrefte
    // at autentiseringsappen er konfigurert.
    [Required(ErrorMessage =
        "Skriv inn koden fra autentiseringsappen.")]
    [RegularExpression(
        @"^[0-9]{6}$",
        ErrorMessage = "Koden må inneholde seks sifre.")]
    public string Code { get; set; } =
        string.Empty;
}


// ========================================
// TOFAKTOR VED INNLOGGING
// ========================================

public class TwoFactorLoginViewModel
{
    [Required(ErrorMessage =
        "Skriv inn engangskoden.")]
    [RegularExpression(
        @"^[0-9]{6}$",
        ErrorMessage = "Koden må inneholde seks sifre.")]
    public string Code { get; set; } =
        string.Empty;
}


// ========================================
// DEAKTIVERING AV TOFAKTOR
// ========================================

public class TwoFactorDisableViewModel
{
    // Passord kreves for å deaktivere
    // tofaktorautentisering.
    [Required(ErrorMessage =
        "Skriv inn passordet ditt.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } =
        string.Empty;
}


// ========================================
// GJENOPPRETTINGSKODER
// ========================================

public class TwoFactorRecoveryCodesViewModel
{
    // Vises én gang etter aktivering.
    // Kodene skal oppbevares sikkert.
    public IReadOnlyList<string> Codes { get; set; } =
        Array.Empty<string>();
}
