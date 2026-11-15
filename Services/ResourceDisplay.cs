using System.Globalization;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.Services;

/// <summary>
/// Felles hjelpere for å vise ressurser pent i sidene:
/// ikon per ressurstype, adresse uten store bokstaver og riktig entall/flertall.
/// </summary>
public static class ResourceDisplay
{
    /// <summary>
    /// Et enkelt SVG-ikon for ressurstypen. Bruker currentColor,
    /// så ikonet får fargen fra elementet det står i (fungerer også i mørk modus).
    /// Brukes med @Html.Raw(ResourceDisplay.Icon(type)).
    /// </summary>
    public static string Icon(ResourceType type)
    {
        string paths = type switch
        {
            // Lastebil
            ResourceType.Transport =>
                "<path d=\"M3 7h11v9H3z\"/><path d=\"M14 10h4l3 3v3h-7z\"/><circle cx=\"7\" cy=\"17.5\" r=\"1.8\"/><circle cx=\"17\" cy=\"17.5\" r=\"1.8\"/>",

            // Drone: kropp og fire rotorer
            ResourceType.Drone =>
                "<rect x=\"9\" y=\"9\" width=\"6\" height=\"6\" rx=\"1.5\"/><path d=\"M9 9 6 6M15 9l3-3M9 15l-3 3M15 15l3 3\"/><circle cx=\"5\" cy=\"5\" r=\"2\"/><circle cx=\"19\" cy=\"5\" r=\"2\"/><circle cx=\"5\" cy=\"19\" r=\"2\"/><circle cx=\"19\" cy=\"19\" r=\"2\"/>",

            // Lyn
            ResourceType.Generator =>
                "<path d=\"M13 2 4 14h7l-1 8 9-12h-7z\"/>",

            // Tannhjul
            ResourceType.Machinery =>
                "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9 7 7M17 17l2.1 2.1M4.9 19.1 7 17M17 7l2.1-2.1\"/>",

            // Radio med antenne
            ResourceType.CommunicationEquipment =>
                "<rect x=\"5\" y=\"9\" width=\"14\" height=\"12\" rx=\"2\"/><path d=\"M9 9 16 3\"/><circle cx=\"12\" cy=\"15\" r=\"2.5\"/>",

            // To personer
            ResourceType.Personnel =>
                "<circle cx=\"9\" cy=\"8\" r=\"3\"/><path d=\"M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6\"/><circle cx=\"17\" cy=\"9\" r=\"2.5\"/><path d=\"M16 14c2.8 0 5 2.2 5 5\"/>",

            // Hus
            ResourceType.Premises =>
                "<path d=\"M3 11 12 4l9 7\"/><path d=\"M5 10v10h14V10\"/><path d=\"M10 20v-5h4v5\"/>",

            // Sandhaug
            ResourceType.SandAndGravel =>
                "<path d=\"M2 19h20\"/><path d=\"M4 19c2-6 5-9 8-9s6 3 8 9\"/><path d=\"M9 15h.01M13 13h.01M15 16h.01\"/>",

            // Båt
            ResourceType.Boat =>
                "<path d=\"M3 15h18l-2.5 4.5h-13z\"/><path d=\"M12 4v11\"/><path d=\"M12 5l6 8h-6\"/>",

            // Eske
            _ =>
                "<path d=\"M3 7.5 12 3l9 4.5v9L12 21l-9-4.5z\"/><path d=\"M3 7.5 12 12l9-4.5M12 12v9\"/>"
        };

        return "<svg viewBox=\"0 0 24 24\" width=\"22\" height=\"22\" fill=\"none\" stroke=\"currentColor\" "
               + "stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">"
               + paths
               + "</svg>";
    }


    /// <summary>
    /// Kartverket skriver poststed og kommune med store bokstaver
    /// («4611, KRISTIANSAND S – KRISTIANSAND kommune»). Her gjøres ord
    /// som bare består av store bokstaver om til vanlig skrivemåte
    /// («Kristiansand S»). Enkeltbokstaver som «S» beholdes.
    /// </summary>
    public static string? PrettyAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return address;
        }

        string[] words = address.Split(' ');

        for (int i = 0; i < words.Length; i++)
        {
            string word = words[i];

            bool hasLetters = word.Any(char.IsLetter);
            bool allUpper = hasLetters && word.Where(char.IsLetter).All(char.IsUpper);
            int letterCount = word.Count(char.IsLetter);

            if (allUpper && letterCount > 1)
            {
                words[i] = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant());
            }
        }

        return string.Join(' ', words);
    }


    /// <summary>
    /// Henter kommunen fra en adresse på formen
    /// «gate, postnummer poststed – kommune kommune».
    /// Returnerer «Ukjent kommune» for eldre adresser uten kommune.
    /// </summary>
    public static string Municipality(string? address)
    {
        const string unknown = "Ukjent kommune";

        if (string.IsNullOrWhiteSpace(address))
        {
            return unknown;
        }

        string[] parts = address.Split(" – ", 2);

        if (parts.Length != 2)
        {
            return unknown;
        }

        string municipality = parts[1].Trim();

        if (municipality.EndsWith(" kommune", StringComparison.OrdinalIgnoreCase))
        {
            municipality = municipality[..^" kommune".Length].Trim();
        }

        return string.IsNullOrWhiteSpace(municipality)
            ? unknown
            : PrettyAddress(municipality)!;
    }


    /// <summary>
    /// «1 ressurs», «2 ressurser».
    /// </summary>
    public static string CountText(int count, string singular, string plural)
    {
        return count + " " + (count == 1 ? singular : plural);
    }
}
