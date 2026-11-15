
using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// ViewModel for registrering av en ressurs.
/// Eierskap settes på serversiden i ResourceController.
/// </summary>
public class RegisterResourceViewModel : IValidatableObject
{
    // RESOURCE INFORMATION

    [Required(ErrorMessage = "Velg type ressurs.")]
[EnumDataType(typeof(ResourceType), ErrorMessage = "Velg en gyldig ressurstype.")]
[Display(Name = "Ressurstype")]
public ResourceType? ResourceType { get; set; }

    [Required(ErrorMessage = "Ressursnavn er påkrevd.")]
[StringLength(100, MinimumLength = 3,
    ErrorMessage = "Ressursnavnet må være mellom 3 og 100 tegn.")]
[Display(Name = "Ressursnavn")]
public string ResourceName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Beskrivelse er påkrevd.")]
[StringLength(500, MinimumLength = 10,
    ErrorMessage = "Beskrivelsen må være mellom 10 og 500 tegn.")]
[Display(Name = "Beskrivelse")]
public string Description { get; set; } = string.Empty;

    [Range(1, 100000, ErrorMessage = "Antall må være minst 1.")]
    [Display(Name = "Antall")]
    public int Quantity { get; set; } = 1;

    [StringLength(200)]
    [Display(Name = "Kapasitet")]
    public string? Capacity { get; set; }

    [StringLength(200)]
    [Display(Name = "Tilgjengelighet")]
    public string? Availability { get; set; }

    // Perioden må være ett av de eksisterende valgene og valideres også på serveren.
    [Required(ErrorMessage = "Velg hvor lenge ressursen skal være tilgjengelig.")]
    [Display(Name = "Hvor lenge skal ressursen være tilgjengelig?")]
    public int? AvailabilityMonths { get; set; }


    // =========================
    // LOCATION

    [StringLength(200)]
    [Display(Name = "Adresse / område")]
    public string? Address { get; set; }

    // Tre skjemafelt samles i det eksisterende adressefeltet ved lagring.
    [StringLength(200)]
    [Display(Name = "Adresse")]
    public string? StreetAddress { get; set; }

    [StringLength(100)]
    [Display(Name = "Postnummer og poststed")]
    public string? PostalLocation { get; set; }

    [StringLength(100)]
    [Display(Name = "Kommune")]
    public string? Municipality { get; set; }

    public string? CombinedAddress()
    {
        if (StreetAddress is null && PostalLocation is null && Municipality is null) return Address?.Trim();
        var streetAndPost = string.Join(", ", new[] { StreetAddress?.Trim(), PostalLocation?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.Join(" – ", new[] { streetAndPost,
            string.IsNullOrWhiteSpace(Municipality) ? null : $"{Municipality.Trim()} kommune" }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }


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

    [Range(-180, 180, ErrorMessage = "Lengdegrad må være mellom -180 og 180.")]
    public double Longitude { get; set; }

    // CONTACT INFORMATION

    [EmailAddress(ErrorMessage = "Skriv inn en gyldig e-postadresse.")]
    [StringLength(150)]
    [Display(Name = "Kontakt-e-post")]
    public string? ContactEmail { get; set; }

    [StringLength(5)]
    [Display(Name = "Landkode")]
    public string? ContactPhoneCountryCode { get; set; } = "+47";

    [StringLength(20)]
    [Phone(ErrorMessage = "Skriv inn et gyldig telefonnummer.")]
    [Display(Name = "Telefonnummer")]
    public string? ContactPhoneNumber { get; set; }

    // CUSTOM VALIDATION

public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
{
    // Sjekk at ressursnavnet har minst 3 tegn etter trimming
    if (!string.IsNullOrWhiteSpace(ResourceName) &&
        ResourceName.Trim().Length < 3)
    {
        yield return new ValidationResult(
            "Ressursnavnet må inneholde minst 3 tegn.",
            new[] { nameof(ResourceName) }
        );
    }

        // Avviser ugyldige perioder og adresser som er for lange etter sammenslåing.
        if (AvailabilityMonths.HasValue
            && !FirstWebAppInDocker.Services.ResourceAvailability.Periods.ContainsKey(AvailabilityMonths.Value))
        {
            yield return new ValidationResult("Velg en gyldig periode.", new[] { nameof(AvailabilityMonths) });
        }
        if (CombinedAddress()?.Length > 200)
        {
            yield return new ValidationResult("Samlet adresse kan maksimalt være 200 tegn.", new[] { nameof(StreetAddress) });
        }
        // =========================
        // MAP POSITION
        // =========================

    // Sjekk at beskrivelsen har minst 10 tegn etter trimming
    if (!string.IsNullOrWhiteSpace(Description) &&
        Description.Trim().Length < 10)
    {
        yield return new ValidationResult(
            "Beskrivelsen må inneholde minst 10 tegn.",
            new[] { nameof(Description) }
        );
    }
        if (
            !double.IsFinite(Latitude) ||
            !double.IsFinite(Longitude) ||
            Latitude < -90 ||
            Latitude > 90 ||
            Longitude < -180 ||
            Longitude > 180
        )
        {
            yield return new ValidationResult(
                "Velg en gyldig plassering i kartet.",
                new[] { nameof(Latitude), nameof(Longitude) }
            );
        }

        if (Latitude == 0 && Longitude == 0)
        {
            yield return new ValidationResult(
                "Velg plassering for ressursen i kartet.",
                new[] { nameof(Latitude), nameof(Longitude) }
            );
        }

        if (string.IsNullOrWhiteSpace(ContactPhoneNumber))
        {
            yield break;
        }

        string phoneNumber = new string(
            ContactPhoneNumber.Where(char.IsDigit).ToArray()
        );

        switch (ContactPhoneCountryCode)
        {
            case "+47":
                if (phoneNumber.Length != 8)
                {
                    yield return new ValidationResult(
                        "Norske telefonnummer må inneholde 8 sifre.",
                        new[] { nameof(ContactPhoneNumber) }
                    );
                }
                break;

            case "+45":
                if (phoneNumber.Length != 8)
                {
                    yield return new ValidationResult(
                        "Danske telefonnummer må inneholde 8 sifre.",
                        new[] { nameof(ContactPhoneNumber) }
                    );
                }
                break;

            case "+46":
                if (phoneNumber.Length < 7 || phoneNumber.Length > 10)
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
}