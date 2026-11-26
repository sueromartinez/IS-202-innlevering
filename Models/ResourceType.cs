/*
    ResourceType.cs – listen over kategorier

    Hva filen gjør: Felles kategoriliste for både ressurser og behov. Fordi begge bruker den
    samme listen, kan matchingen sammenligne dem direkte.

    På nettsiden: Valgene i nedtrekkslisten «Type» i skjemaene, med de norske navnene fra
    [Display].
*/

using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/*
    Definerer ressurskategoriene som kan registreres
    og brukes til matching mellom behov og ressurser.

    Tallverdiene er eksplisitte fordi ResourceType lagres
    som heltall i eksisterende databaseoppføringer.

    Verdiene må ikke endres uten en kontrollert
    databasemigrering som oppdaterer eksisterende data.
*/
public enum ResourceType
{
    // Transportressurser.
    [Display(Name = "Transport")]
    Transport = 0,

    // Droner og droneoperatører.
    [Display(Name = "Drone / droneoperatør")]
    Drone = 1,

    // Strømforsyning og aggregater.
    [Display(Name = "Strøm / aggregat")]
    Generator = 2,

    // Maskiner og maskinelt utstyr.
    [Display(Name = "Maskiner")]
    Machinery = 3,

    // Sambands- og kommunikasjonsutstyr.
    [Display(Name = "Sambandsutstyr")]
    CommunicationEquipment = 4,

    // Mannskap og frivillige.
    [Display(Name = "Mannskap / frivillige")]
    Personnel = 5,

    // Lokaler og bygninger.
    [Display(Name = "Lokaler")]
    Premises = 6,

    // Sand, grus og tilsvarende masser.
    [Display(Name = "Sand / grus")]
    SandAndGravel = 7,

    // Båter og maritime ressurser.
    [Display(Name = "Båt")]
    Boat = 8,

    // Ressurser som ikke passer i de øvrige kategoriene.
    [Display(Name = "Annet")]
    Other = 9
}
