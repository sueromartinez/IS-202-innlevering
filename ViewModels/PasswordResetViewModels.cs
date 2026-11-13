
using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.ViewModels;

// ========================================
// GLEMT PASSORD
// ========================================

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "E-post er påkrevd.")]
    [EmailAddress(ErrorMessage = "Skriv inn en gyldig e-postadresse.")]
    [MaxLength(254)]
    [Display(Name = "E-post")]
    public string Email { get; set; } = string.Empty;
}


// ========================================
// TILBAKESTILL PASSORD
// ========================================

public class ResetPasswordViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nytt passord er påkrevd.")]
    [StringLength(
        200,
        MinimumLength = 8,
        ErrorMessage = "Passordet må inneholde minst 8 tegn.")]
    [DataType(DataType.Password)]
    [Display(Name = "Nytt passord")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bekreft passordet.")]
    [DataType(DataType.Password)]
    [Compare(
        nameof(NewPassword),
        ErrorMessage = "Passordene må være like.")]
    [Display(Name = "Bekreft nytt passord")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
