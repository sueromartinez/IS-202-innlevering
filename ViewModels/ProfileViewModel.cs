using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Representerer informasjonen som
/// vises og redigeres på profilsiden.
/// </summary>
public class ProfileViewModel : IValidatableObject
{
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


    /// <summary>
    /// E-post hentes fra ASP.NET Core Identity.
    /// Den kan foreløpig ikke endres fra profilsiden.
    /// </summary>
    public string Email { get; set; } =
        string.Empty;


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


    /// <summary>
    /// Tidspunkt kontoen ble opprettet.
    /// Kun visningsdata.
    /// </summary>
    public DateTime CreatedAt { get; set; }


    /// <summary>
    /// Viser om brukeren har aktivert 2FA.
    /// Kun visningsdata.
    /// </summary>
    public bool TwoFactorEnabled { get; set; }


    /// <summary>
    /// Brukerens primære Identity-rolle.
    /// Kun visningsdata.
    /// </summary>
    public string Role { get; set; } =
        string.Empty;


    /// <summary>
    /// Validerer telefonnummer basert
    /// på valgt landskode.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext
    )
    {
        if (
            string.IsNullOrWhiteSpace(
                PhoneNumber
            )
        )
        {
            yield break;
        }


        if (
            !Regex.IsMatch(
                PhoneNumber,
                @"^[0-9\s-]+$"
            )
        )
        {
            yield return new ValidationResult(
                "Telefonnummeret kan bare inneholde sifre, mellomrom og bindestrek.",
                new[]
                {
                    nameof(PhoneNumber)
                }
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

                if (
                    normalizedPhoneNumber.Length
                    != 8
                )
                {
                    yield return new ValidationResult(
                        "Norske telefonnumre må inneholde nøyaktig 8 sifre.",
                        new[]
                        {
                            nameof(PhoneNumber)
                        }
                    );
                }

                break;


            case "+45":

                if (
                    normalizedPhoneNumber.Length
                    != 8
                )
                {
                    yield return new ValidationResult(
                        "Danske telefonnumre må inneholde nøyaktig 8 sifre.",
                        new[]
                        {
                            nameof(PhoneNumber)
                        }
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
                        new[]
                        {
                            nameof(PhoneNumber)
                        }
                    );
                }

                break;


            default:

                yield return new ValidationResult(
                    "Velg en gyldig landskode.",
                    new[]
                    {
                        nameof(PhoneCountryCode)
                    }
                );

                break;
        }
    }
}