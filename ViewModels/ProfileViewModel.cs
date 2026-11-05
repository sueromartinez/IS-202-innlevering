using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Representerer informasjonen som
/// vises og redigeres på profilsiden.
///
/// Validerer navn, e-post og telefonnummer.
/// </summary>
public class ProfileViewModel : IValidatableObject
{
    // ========================================
    // NAVN
    // ========================================

    [Required(
        ErrorMessage = "Navn er påkrevd"
    )]
    [StringLength(
        100,
        ErrorMessage = "Navnet kan maks være 100 tegn"
    )]
    [Display(Name = "Navn")]
    public string Name { get; set; } =
        string.Empty;


    // ========================================
    // E-POST
    // ========================================

    [Required(
        ErrorMessage = "E-post er påkrevd"
    )]
    [EmailAddress(
        ErrorMessage = "Skriv inn en gyldig e-postadresse"
    )]
    [StringLength(
        150,
        ErrorMessage = "E-postadressen er for lang"
    )]
    [Display(Name = "E-post")]
    public string Email { get; set; } =
        string.Empty;


    // ========================================
    // TELEFON
    // ========================================

    [Required(
        ErrorMessage = "Landskode er påkrevd"
    )]
    [StringLength(5)]
    [Display(Name = "Landskode")]
    public string PhoneCountryCode { get; set; } =
        "+47";


    [StringLength(
        20,
        ErrorMessage = "Telefonnummeret er for langt"
    )]
    [Display(Name = "Telefonnummer")]
    public string? PhoneNumber { get; set; }


    // ========================================
    // VISNINGSDATA
    // ========================================

    public DateTime CreatedAt { get; set; }

    public bool TwoFactorEnabled { get; set; }

    public string Role { get; set; } =
        string.Empty;


    // ========================================
    // VALIDERING AV TELEFONNUMMER
    // ========================================

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext
    )
    {
        if (
            PhoneCountryCode != "+47"
            &&
            PhoneCountryCode != "+45"
            &&
            PhoneCountryCode != "+46"
        )
        {
            yield return new ValidationResult(
                "Velg en gyldig landskode.",
                new[] { nameof(PhoneCountryCode) }
            );

            yield break;
        }


        // Telefonnummer er valgfritt.

        if (
            string.IsNullOrWhiteSpace(
                PhoneNumber
            )
        )
        {
            yield break;
        }


        // Kun sifre, mellomrom og bindestreker.

        if (
            !Regex.IsMatch(
                PhoneNumber,
                @"^[0-9\s-]+$"
            )
        )
        {
            yield return new ValidationResult(
                "Telefonnummeret kan bare inneholde sifre, mellomrom og bindestrek.",
                new[] { nameof(PhoneNumber) }
            );

            yield break;
        }


        // Fjern mellomrom og bindestreker
        // før vi kontrollerer lengden.

        string normalizedPhoneNumber =
            Regex.Replace(
                PhoneNumber,
                @"[\s-]",
                ""
            );


        switch (PhoneCountryCode)
        {
            case "+47":

                if (
                    normalizedPhoneNumber.Length != 8
                )
                {
                    yield return new ValidationResult(
                        "Norske telefonnumre må inneholde nøyaktig 8 sifre.",
                        new[] { nameof(PhoneNumber) }
                    );
                }

                break;


            case "+45":

                if (
                    normalizedPhoneNumber.Length != 8
                )
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