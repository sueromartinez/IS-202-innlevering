using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models
{
    public class PositionModel
    {
        [Required(ErrorMessage = "Du må skrive inn type ressurs")]
        [Display(Name = "Type ressurs")]
        public string ResourceType { get; set; }

        [Required(ErrorMessage = "Beskrivelse er påkrevd")]
        [Display(Name = "Beskrivelse")]
        public string Description { get; set; }

        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }

        [Display(Name = "Kontaktinfo")]
        public string? ContactInfo { get; set; }
    }
}
