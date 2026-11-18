
namespace FirstWebAppInDocker.Models;

/// <summary>
/// Representerer en geografisk posisjon med breddegrad
/// og lengdegrad i desimalgrader.
/// </summary>
/// <param name="Latitude">
/// Geografisk breddegrad i intervallet -90 til 90.
/// </param>
/// <param name="Longitude">
/// Geografisk lengdegrad i intervallet -180 til 180.
/// </param>
/// <remarks>
/// Coordinate er en uforanderlig verditype som kan brukes
/// til å overføre og sammenligne geografiske posisjoner
/// uten å opprette separate koordinatobjekter.
/// </remarks>
public readonly record struct Coordinate(
    double Latitude,
    double Longitude);
