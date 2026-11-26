/*
    Coordinate.cs – en kartposisjon

    Hva filen gjør: En liten type som holder breddegrad og lengdegrad sammen, slik at vi kan
    sende én verdi i stedet for to tall.
*/

namespace FirstWebAppInDocker.Models;

/*
    Representerer en geografisk posisjon med breddegrad
    og lengdegrad i desimalgrader.

    Latitude:
    Geografisk breddegrad i intervallet -90 til 90.

    Longitude:
    Geografisk lengdegrad i intervallet -180 til 180.

    Coordinate er en uforanderlig verditype som kan brukes
    til å overføre og sammenligne geografiske posisjoner
    uten å opprette separate koordinatobjekter.
*/
public readonly record struct Coordinate(double Latitude, double Longitude);
