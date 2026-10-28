using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Representerer data brukeren fyller inn
/// når en ny ressurs registreres.
/// </summary>
public class RegisterResourceViewModel : IValidatableObject
{
    [Required(
        ErrorMessage = "Du må velge type ressurs"
    )]
    [Display(Name = "Type ressurs")]
    public ResourceType? ResourceType { get; init; }


    [Required(
        ErrorMessage = "Beskrivelse er påkrevd"
    )]
    [StringLength(
        500,
        ErrorMessage = "Beskrivelsen kan maks være 500 tegn"
    )]
    [Display(Name = "Beskrivelse")]
    public string Description { get; init; } = string.Empty;


    [EmailAddress(
        ErrorMessage = "Skriv inn en gyldig e-postadresse"
    )]
    [StringLength(
        150,
        ErrorMessage = "E-postadressen er for lang"
    )]
    [Display(Name = "E-post")]
    public string? ContactEmail { get; init; }


    [Display(Name = "Landskode")]
    public string ContactPhoneCountryCode { get; init; } = "+47";


    [StringLength(
        20,
        ErrorMessage = "Telefonnummeret er for langt"
    )]
    [Display(Name = "Telefonnummer")]
    public string? ContactPhoneNumber { get; init; }


    /// <summary>
    /// Breddegrad valgt i kartet.
    /// </summary>
    public double Latitude { get; init; }


    /// <summary>
    /// Lengdegrad valgt i kartet.
    /// </summary>
    public double Longitude { get; init; }


    /// <summary>
    /// Validerer telefonnummer basert på landskode.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext
    )
    {
        if (string.IsNullOrWhiteSpace(ContactPhoneNumber))
        {
            yield break;
        }


        if (!Regex.IsMatch(
                ContactPhoneNumber,
                @"^[0-9\s-]+$"
            ))
        {
            yield return new ValidationResult(
                "Telefonnummeret kan bare inneholde sifre, mellomrom og bindestrek.",
                new[] { nameof(ContactPhoneNumber) }
            );

            yield break;
        }


        string phoneNumber =
            Regex.Replace(
                ContactPhoneNumber,
                @"[\s-]",
                ""
            );


        switch (ContactPhoneCountryCode)
        {
            case "+47":

                if (phoneNumber.Length != 8)
                {
                    yield return new ValidationResult(
                        "Telefonnummer for Norge må inneholde nøyaktig 8 sifre.",
                        new[] { nameof(ContactPhoneNumber) }
                    );
                }

                break;


            case "+45":

                if (phoneNumber.Length != 8)
                {
                    yield return new ValidationResult(
                        "Telefonnummer for Danmark må inneholde nøyaktig 8 sifre.",
                        new[] { nameof(ContactPhoneNumber) }
                    );
                }

                break;


            case "+46":

                if (
                    phoneNumber.Length < 7
                    ||
                    phoneNumber.Length > 10
                )
                {
                    yield return new ValidationResult(
                        "Telefonnummer for Sverige må inneholde mellom 7 og 10 sifre.",
                        new[] { nameof(ContactPhoneNumber) }
                    );
                }

                break;
        }
    }
}