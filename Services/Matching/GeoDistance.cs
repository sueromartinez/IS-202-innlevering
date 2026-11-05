namespace FirstWebAppInDocker.Services.Matching;

/// <summary>
/// Avstandsberegning mellom to punkter på jorden.
///
/// Dette gjøres deterministisk i C#, ikke av språkmodellen.
/// Sami får ferdig beregnede avstander og skal bare forklare
/// og rangere dem.
/// </summary>
public static class GeoDistance
{
    private const double EarthRadiusKm = 6371.0;


    /// <summary>
    /// Haversine-formelen: avstand i luftlinje (km) mellom to koordinater.
    /// </summary>
    public static double HaversineKm(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2
    )
    {
        double deltaLatitude = ToRadians(latitude2 - latitude1);
        double deltaLongitude = ToRadians(longitude2 - longitude1);

        double a =
            Math.Sin(deltaLatitude / 2) * Math.Sin(deltaLatitude / 2)
            + Math.Cos(ToRadians(latitude1))
            * Math.Cos(ToRadians(latitude2))
            * Math.Sin(deltaLongitude / 2) * Math.Sin(deltaLongitude / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusKm * c;
    }


    /// <summary>
    /// Et grovt rektangel rundt et punkt. Brukes til å filtrere i
    /// databasen før den nøyaktige Haversine-beregningen, slik at
    /// vi slipper å hente alle ressurser i landet.
    /// </summary>
    public static (double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude)
        BoundingBox(double latitude, double longitude, double radiusKm)
    {
        double latitudeDelta = radiusKm / 111.0;

        // En lengdegrad blir kortere jo lenger nord man kommer.
        double cosLatitude = Math.Max(Math.Cos(ToRadians(latitude)), 0.01);
        double longitudeDelta = radiusKm / (111.0 * cosLatitude);

        return (
            latitude - latitudeDelta,
            latitude + latitudeDelta,
            longitude - longitudeDelta,
            longitude + longitudeDelta
        );
    }


    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }
}
