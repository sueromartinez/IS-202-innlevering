using System.Globalization;
using System.Text.Json;

namespace FirstWebAppInDocker.Services.Ai;

public record GeocodeResult(double Latitude, double Longitude, string DisplayName);


/// <summary>
/// Gjør et stedsnavn ("Grimstad sentrum") om til koordinater
/// via Nominatim (OpenStreetMap), samme tjeneste som kartet bruker.
///
/// Kalles fra serveren, ikke fra modellen direkte. Nominatim krever
/// en beskrivende User-Agent og maks ett kall i sekundet.
/// </summary>
public class NominatimGeocoder
{
    private readonly HttpClient _httpClient;

    private readonly ILogger<NominatimGeocoder> _logger;


    public NominatimGeocoder(
        HttpClient httpClient,
        ILogger<NominatimGeocoder> logger
    )
    {
        _logger = logger;

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Beredskapsportal-studentprosjekt/1.0");
    }


    /// <summary>
    /// Returnerer null hvis stedet ikke finnes eller tjenesten ikke svarer.
    /// Søket begrenses til Norge.
    /// </summary>
    public async Task<GeocodeResult?> GeocodeAsync(
        string place,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(place))
        {
            return null;
        }

        string url =
            "search?format=jsonv2&limit=1&countrycodes=no&q="
            + Uri.EscapeDataString(place.Trim());

        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument document =
                await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);

            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return null;
            }

            JsonElement first = root[0];

            // Nominatim returnerer koordinatene som tekst.
            double latitude = double.Parse(
                first.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);

            double longitude = double.Parse(
                first.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);

            string displayName =
                first.TryGetProperty("display_name", out JsonElement name)
                    ? name.GetString() ?? place
                    : place;

            return new GeocodeResult(latitude, longitude, displayName);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            _logger.LogWarning(exception, "Geokoding feilet for {Place}", place);
            return null;
        }
    }
}
