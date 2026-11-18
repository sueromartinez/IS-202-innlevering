
using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Definerer ressurskategoriene som kan registreres
/// og brukes til matching mellom behov og ressurser.
/// </summary>
/// <remarks>
/// Tallverdiene er eksplisitte fordi ResourceType lagres
/// som heltall i eksisterende databaseoppføringer.
///
/// Verdiene må ikke endres uten en kontrollert
/// databasemigrering som oppdaterer eksisterende data.
/// </remarks>
public enum ResourceType
{
    /// <summary>
    /// Transportressurser.
    /// </summary>
    [Display(Name = "Transport")]
    Transport = 0,

    /// <summary>
    /// Droner og droneoperatører.
    /// </summary>
    [Display(Name = "Drone / droneoperatør")]
    Drone = 1,

    /// <summary>
    /// Strømforsyning og aggregater.
    /// </summary>
    [Display(Name = "Strøm / aggregat")]
    Generator = 2,

    /// <summary>
    /// Maskiner og maskinelt utstyr.
    /// </summary>
    [Display(Name = "Maskiner")]
    Machinery = 3,

    /// <summary>
    /// Sambands- og kommunikasjonsutstyr.
    /// </summary>
    [Display(Name = "Sambandsutstyr")]
    CommunicationEquipment = 4,

    /// <summary>
    /// Mannskap og frivillige.
    /// </summary>
    [Display(Name = "Mannskap / frivillige")]
    Personnel = 5,

    /// <summary>
    /// Lokaler og bygninger.
    /// </summary>
    [Display(Name = "Lokaler")]
    Premises = 6,

    /// <summary>
    /// Sand, grus og tilsvarende masser.
    /// </summary>
    [Display(Name = "Sand / grus")]
    SandAndGravel = 7,

    /// <summary>
    /// Båter og maritime ressurser.
    /// </summary>
    [Display(Name = "Båt")]
    Boat = 8,

    /// <summary>
    /// Ressurser som ikke passer i de øvrige kategoriene.
    /// </summary>
    [Display(Name = "Annet")]
    Other = 9
}
