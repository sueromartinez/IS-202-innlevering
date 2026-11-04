using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// ViewModel for registrering av en ressurs.
///
/// Inneholder kun data som brukeren selv
/// skal kunne sende inn fra registreringsskjemaet.
///
/// Eierskap som OwnerUserId og OwnerEmail
/// settes på serversiden i ResourceController.
/// </summary>
public class RegisterResourceViewModel
    : IValidatableObject
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
        ErrorMessage = "Antall må være minst 1."
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


    /// <summary>
    /// Breddegrad valgt i Leaflet-kartet.
    /// </summary>
    [Range(
        -90,
        90,
        ErrorMessage =
            "Breddegrad må være mellom -90 og 90."
    )]
    public double Latitude { get; set; }


    /// <summary>
    /// Lengdegrad valgt i Leaflet-kartet.
    /// </summary>
    [Range(
        -180,
        180,
        ErrorMessage =
            "Lengdegrad må være mellom -180 og 180."
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
        150,
        ErrorMessage =
            "E-postadressen kan maksimalt være 150 tegn."
    )]
    [Display(
        Name = "Kontakt-e-post"
    )]
    public string? ContactEmail { get; set; }


    [StringLength(
        5,
        ErrorMessage =
            "Landkoden kan maksimalt være 5 tegn."
    )]
    [Display(
        Name = "Landkode"
    )]
    public string? ContactPhoneCountryCode { get; set; }
        = "+47";


    [StringLength(
        20,
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


    // =========================
    // CUSTOM VALIDATION
    // =========================

    /// <summary>
    /// Utfører validering som avhenger av flere felt,
    /// blant annet kartplassering og telefonnummer.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext
    )
    {
        // =========================
        // MAP POSITION
        // =========================

        if (
            !double.IsFinite(Latitude)
            ||
            !double.IsFinite(Longitude)
            ||
            Latitude < -90
            ||
            Latitude > 90
            ||
            Longitude < -180
            ||
            Longitude > 180
        )
        {
            yield return new ValidationResult(
                "Velg en gyldig plassering i kartet.",
                new[]
                {
                    nameof(Latitude),
                    nameof(Longitude)
                }
            );
        }


        // 0,0 brukes som standardverdi dersom
        // brukeren ikke har klikket i kartet.
        //
        // Et punkt på 0,0 er teknisk gyldig geografisk,
        // men er ikke en realistisk norsk ressursplassering
        // i denne applikasjonen.
        if (
            Latitude == 0
            &&
            Longitude == 0
        )
        {
            yield return new ValidationResult(
                "Velg plassering for ressursen i kartet.",
                new[]
                {
                    nameof(Latitude),
                    nameof(Longitude)
                }
            );
        }


        // =========================
        // PHONE NUMBER
        // =========================

        if (
            string.IsNullOrWhiteSpace(
                ContactPhoneNumber
            )
        )
        {
            yield break;
        }


        string phoneNumber =
            new string(
                ContactPhoneNumber
                    .Where(char.IsDigit)
                    .ToArray()
            );


        switch (
            ContactPhoneCountryCode
        )
        {
            case "+47":

                if (phoneNumber.Length != 8)
                {
                    yield return new ValidationResult(
                        "Norske telefonnummer må inneholde 8 sifre.",
                        new[]
                        {
                            nameof(ContactPhoneNumber)
                        }
                    );
                }

                break;


            case "+45":

                if (phoneNumber.Length != 8)
                {
                    yield return new ValidationResult(
                        "Danske telefonnummer må inneholde 8 sifre.",
                        new[]
                        {
                            nameof(ContactPhoneNumber)
                        }
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
                        new[]
                        {
                            nameof(ContactPhoneNumber)
                        }
                    );
                }

                break;


            default:

                yield return new ValidationResult(
                    "Ugyldig landskode.",
                    new[]
                    {
                        nameof(ContactPhoneCountryCode)
                    }
                );

                break;
        }
    }
}