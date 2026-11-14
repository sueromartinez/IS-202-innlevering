using System.Globalization;
using System.Text.Json;

namespace FirstWebAppInDocker.Services.Ai;

/// <summary>
/// Resultatet av et adresseoppslag, delt opp slik
/// registreringsskjemaet for ressurser vil ha det.
/// </summary>
public record AddressResult(
    string StreetAddress,
    string? PostalLocation,
    string? Municipality,
    double Latitude,
    double Longitude
);


/// <summary>
/// Gjør en adresse eller et stedsnavn om til en plassering.
/// Eget grensesnitt, slik at testene kan bruke et falskt oppslag uten nettverk.
/// </summary>
public interface IAddressLookup
{
    Task<AddressResult?> LookupAsync(string text, CancellationToken cancellationToken);
}


/// <summary>
/// Slår opp adresser hos Kartverket, samme tjeneste som adressefeltet
/// i registreringsskjemaet bruker. Finner Kartverket ingen adresse
/// (f.eks. bare et stedsnavn som «Grimstad sentrum»), brukes Nominatim.
/// </summary>
public class KartverketAddressLookup : IAddressLookup
{
    private readonly HttpClient _httpClient;

    private readonly NominatimGeocoder _nominatim;

    private readonly ILogger<KartverketAddressLookup> _logger;


    public KartverketAddressLookup(
        HttpClient httpClient,
        NominatimGeocoder nominatim,
        ILogger<KartverketAddressLookup> logger
    )
    {
        _nominatim = nominatim;
        _logger = logger;

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.kartverket.no/");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }


    public async Task<AddressResult?> LookupAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string query = text.Trim();

        AddressResult? address = await SearchKartverketAsync(query, cancellationToken);

        if (address is not null)
        {
            return address;
        }

        // Stedsnavn uten gateadresse: bruk Nominatim og behold teksten som adresse.
        GeocodeResult? place = await _nominatim.GeocodeAsync(query, cancellationToken);

        if (place is null)
        {
            return null;
        }

        return new AddressResult(
            query.Length > 200 ? query[..200] : query,
            null,
            null,
            place.Latitude,
            place.Longitude);
    }


    private async Task<AddressResult?> SearchKartverketAsync(string query, CancellationToken cancellationToken)
    {
        string url =
            "adresser/v1/sok?treffPerSide=1&utkoordsys=4326&sok="
            + Uri.EscapeDataString(query);

        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument document =
                await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("adresser", out JsonElement addresses)
                || addresses.ValueKind != JsonValueKind.Array
                || addresses.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement item = addresses[0];

            if (!item.TryGetProperty("representasjonspunkt", out JsonElement point)
                || !TryGetNumber(point, "lat", out double latitude)
                || !TryGetNumber(point, "lon", out double longitude))
            {
                return null;
            }

            string? street = GetString(item, "adressetekst");
            string? postalCode = GetString(item, "postnummer");
            string? postalPlace = GetString(item, "poststed");
            string? municipality = GetString(item, "kommunenavn");

            if (string.IsNullOrWhiteSpace(street))
            {
                return null;
            }

            string? postalLocation =
                string.Join(", ", new[] { postalCode, postalPlace }.Where(value => !string.IsNullOrWhiteSpace(value)));

            return new AddressResult(
                street,
                string.IsNullOrWhiteSpace(postalLocation) ? null : postalLocation,
                ToTitleCase(municipality),
                latitude,
                longitude);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(exception, "Adresseoppslag hos Kartverket feilet for {Query}", query);
            return null;
        }
    }


    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }


    private static bool TryGetNumber(JsonElement element, string name, out double number)
    {
        number = 0;

        return element.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out number)
            && double.IsFinite(number);
    }


    /// <summary>
    /// Kartverket skriver kommunenavn med store bokstaver ("GRIMSTAD").
    /// </summary>
    private static string? ToTitleCase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
    }
}
