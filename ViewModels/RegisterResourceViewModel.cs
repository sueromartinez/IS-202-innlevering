using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Data som brukes når en ressursleverandør
/// registrerer en ressurs.
/// </summary>
public class RegisterResourceViewModel
{
    // =========================
    // RESOURCE INFORMATION
    // =========================

    [Required(
        ErrorMessage = "Velg type ressurs."
    )]
    [Display(
        Name = "Ressurstype"
    )]
    public ResourceType? ResourceType { get; set; }


    [Required(
        ErrorMessage = "Navn på ressursen er påkrevd."
    )]
    [StringLength(
        100,
        ErrorMessage =
            "Ressursnavnet kan maksimalt være 100 tegn."
    )]
    [Display(
        Name = "Navn på ressurs"
    )]
    public string ResourceName { get; set; }
        = string.Empty;


    [Required(
        ErrorMessage = "Beskrivelse er påkrevd."
    )]
    [StringLength(
        500,
        ErrorMessage =
            "Beskrivelsen kan maksimalt være 500 tegn."
    )]
    [Display(
        Name = "Beskrivelse"
    )]
    public string Description { get; set; }
        = string.Empty;


    [Range(
        1,
        100000,
        ErrorMessage =
            "Antall må være minst 1."
    )]
    [Display(
        Name = "Antall"
    )]
    public int Quantity { get; set; }
        = 1;


    [StringLength(
        200,
        ErrorMessage =
            "Kapasitet kan maksimalt være 200 tegn."
    )]
    [Display(
        Name = "Kapasitet"
    )]
    public string? Capacity { get; set; }


    [StringLength(
        200,
        ErrorMessage =
            "Tilgjengelighet kan maksimalt være 200 tegn."
    )]
    [Display(
        Name = "Tilgjengelighet"
    )]
    public string? Availability { get; set; }


    // =========================
    // LOCATION
    // =========================

    [StringLength(
        200,
        ErrorMessage =
            "Adresse kan maksimalt være 200 tegn."
    )]
    [Display(
        Name = "Adresse / område"
    )]
    public string? Address { get; set; }


    [Range(
        -90,
        90,
        ErrorMessage =
            "Ugyldig breddegrad."
    )]
    public double Latitude { get; set; }


    [Range(
        -180,
        180,
        ErrorMessage =
            "Ugyldig lengdegrad."
    )]
    public double Longitude { get; set; }


    // =========================
    // CONTACT INFORMATION
    // =========================

    [EmailAddress(
        ErrorMessage =
            "Skriv inn en gyldig e-postadresse."
    )]
    [StringLength(
        150
    )]
    [Display(
        Name = "Kontakt-e-post"
    )]
    public string? ContactEmail { get; set; }


    [StringLength(
        5
    )]
    [Display(
        Name = "Landkode"
    )]
    public string? ContactPhoneCountryCode { get; set; }
        = "+47";


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
    /// Validerer telefonnummer basert på valgt landskode.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext
    )
    {
        // Rejects unexpected status values so resource statistics stay reliable.
        if (!string.IsNullOrWhiteSpace(Availability)
            && Availability != "Tilgjengelig" && Availability != "Ikke tilgjengelig")
        {
            yield return new ValidationResult("Velg en gyldig tilgjengelighet.", new[] { nameof(Availability) });
        }
        if (!Enum.IsDefined(typeof(ResourceType), ResourceType ?? (ResourceType)(-1)))
        {
            yield return new ValidationResult("Velg en gyldig ressurstype.", new[] { nameof(ResourceType) });
        }
        if (!double.IsFinite(Latitude) || !double.IsFinite(Longitude)
            || Latitude < -90 || Latitude > 90 || Longitude < -180 || Longitude > 180)
        {
            yield return new ValidationResult("Velg en gyldig plassering i kartet.", new[] { nameof(Latitude), nameof(Longitude) });
        }

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
            case "+45":

                if (phoneNumber.Length != 8)
                {
                    yield return new ValidationResult(
                        "Telefonnummeret må inneholde nøyaktig 8 sifre.",
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
                        "Svenske telefonnummer må inneholde mellom 7 og 10 sifre.",
                        new[] { nameof(ContactPhoneNumber) }
                    );
                }

                break;


            default:

                yield return new ValidationResult(
                    "Ugyldig landskode.",
                    new[] { nameof(ContactPhoneCountryCode) }
                );

                break;
        }
    }
        ErrorMessage =
            "Telefonnummeret kan maksimalt være 20 tegn."
    )]
    [Phone(
        ErrorMessage =
            "Skriv inn et gyldig telefonnummer."
    )]
    [Display(
        Name = "Telefonnummer"
    )]
    public string? ContactPhoneNumber { get; set; }
}