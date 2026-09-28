// Gir tilgang til attributter som Required og Display.
// Disse brukes til validering og visningsnavn i skjemaer.
using System.ComponentModel.DataAnnotations;

// Samler modellklassene under prosjektets Models-navnerom.
namespace FirstWebAppInDocker.Models
{
    // Modellen beskriver en ressurs med beskrivelse,
    // geografisk posisjon og eventuell kontaktinformasjon.
    public class PositionModel
    {
        // Gjør feltet obligatorisk og viser denne feilmeldingen
        // dersom brukeren ikke skriver inn en ressurstype.
        [Required(ErrorMessage = "Du må skrive inn type ressurs")]

        // Navnet som brukes når skjemaet viser feltets etikett.
        [Display(Name = "Type ressurs")]

        // Lagrer typen ressurs som tekst, for eksempel "Mat" eller "Transport".
        // get betyr at verdien kan leses, og set betyr at den kan endres.
        public string ResourceType { get; set; }

        // Krever at brukeren fyller inn en beskrivelse.
        [Required(ErrorMessage = "Beskrivelse er påkrevd")]

        // Viser "Beskrivelse" som feltets navn i skjemaet.
        [Display(Name = "Beskrivelse")]

        // Lagrer informasjon om ressursen.
        // Eksempel: "Har plass til tre personer i bilen".
        public string Description { get; set; }

        // Markerer feltet som påkrevd.
        // Merk: double kan ikke være null og har standardverdien 0.
        // Required alene sikrer derfor ikke at koordinaten er oppgitt.
        [Required]

        // Breddegrad: hvor langt nord eller sør posisjonen ligger.
        // double brukes fordi koordinater kan inneholde desimaler.
        // Eksempel: 58.1599.
        public double Latitude { get; set; }

        // Samme begrensning med Required gjelder her som for Latitude.
        [Required]

        // Lengdegrad: hvor langt øst eller vest posisjonen ligger.
        // Eksempel: 8.0182.
        public double Longitude { get; set; }

        // Viser "Kontaktinfo" som feltets navn i skjemaet.
        [Display(Name = "Kontaktinfo")]

        // Lagrer for eksempel telefonnummer eller e-postadresse.
        // Spørsmålstegnet i string? betyr at verdien kan være null.
        // Feltet er valgfritt siden det ikke har Required.
        public string? ContactInfo { get; set; }
    }
}