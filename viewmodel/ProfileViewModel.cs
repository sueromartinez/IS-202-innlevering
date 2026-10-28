using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Representerer kontaktinformasjonen
/// som vises og redigeres på profilsiden.
/// </summary>
public class ProfileViewModel : IValidatableObject
{
    [Required(ErrorMessage = "Navn er påkrevd")]
    [StringLength(
        100,
        ErrorMessage = "Navnet kan maks være 100 tegn"
    )]
    [Display(Name = "Navn")]
    public string Name { get; init; } = string.Empty;


    [Required(ErrorMessage = "E-post er påkrevd")]
    [EmailAddress(
        ErrorMessage = "Skriv inn en gyldig e-postadresse"
    )]
    [StringLength(
        150,
        ErrorMessage = "E-postadressen er for lang"
    )]
    [Display(Name = "Kontakt-e-post")]
    public string Email { get; init; } = string.Empty;


    [Required(ErrorMessage = "Landskode er påkrevd")]
    [StringLength(5)]
    [Display(Name = "Landskode")]
    public string PhoneCountryCode { get; init; } = "+47";


    [StringLength(
        20,
        ErrorMessage = "Telefonnummeret er for langt"
    )]
    [Display(Name = "Telefonnummer")]
    public string? PhoneNumber { get; init; }


    /// <summary>
    /// Validerer telefonnummer basert på valgt landskode.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext
    )
    {
        if (string.IsNullOrWhiteSpace(PhoneNumber))
        {
            yield break;
        }


        if (!Regex.IsMatch(
                PhoneNumber,
                @"^[0-9\s-]+$"
            ))
        {
            yield return new ValidationResult(
                "Telefonnummeret kan bare inneholde sifre, mellomrom og bindestrek.",
                new[] { nameof(PhoneNumber) }
            );

            yield break;
        }


        string normalizedPhoneNumber =
            Regex.Replace(
                PhoneNumber,
                @"[\s-]",
                ""
            );


        switch (PhoneCountryCode)
        {
            case "+47":

                if (normalizedPhoneNumber.Length != 8)
                {
                    yield return new ValidationResult(
                        "Norske telefonnumre må inneholde nøyaktig 8 sifre.",
                        new[] { nameof(PhoneNumber) }
                    );
                }

                break;


            case "+45":

                if (normalizedPhoneNumber.Length != 8)
                {
                    yield return new ValidationResult(
                        "Danske telefonnumre må inneholde nøyaktig 8 sifre.",
                        new[] { nameof(PhoneNumber) }
                    );
                }

                break;


            case "+46":

                if (
                    normalizedPhoneNumber.Length < 7
                    ||
                    normalizedPhoneNumber.Length > 10
                )
                {
                    yield return new ValidationResult(
                        "Svenske telefonnumre må inneholde mellom 7 og 10 sifre.",
                        new[] { nameof(PhoneNumber) }
                    );
                }

                break;
        }
    }
}