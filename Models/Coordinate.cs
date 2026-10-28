namespace FirstWebAppInDocker.Models;

/// <summary>
/// Representerer en geografisk posisjon
/// med breddegrad og lengdegrad.
/// </summary>
public readonly record struct Coordinate(
    double Latitude,
    double Longitude
);