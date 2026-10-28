using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Typer ressurser som kan registreres i beredskapsportalen.
/// </summary>
public enum ResourceType
{
    [Display(Name = "Transport")]
    Transport,

    [Display(Name = "Drone / droneoperatør")]
    Drone,

    [Display(Name = "Strøm / aggregat")]
    Generator,

    [Display(Name = "Maskiner")]
    Machinery,

    [Display(Name = "Sambandsutstyr")]
    CommunicationEquipment,

    [Display(Name = "Mannskap / frivillige")]
    Personnel,

    [Display(Name = "Lokaler")]
    Premises,

    [Display(Name = "Sand / grus")]
    SandAndGravel,

    [Display(Name = "Båt")]
    Boat,

    [Display(Name = "Annet")]
    Other
}