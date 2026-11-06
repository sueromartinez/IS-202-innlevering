using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// Data som vises på dashboardet til offentlig aktør.
/// Hentes fra databasen av HomeController.
/// </summary>
public class DashboardViewModel
{
    /// <summary>Alle registrerte behov, nyeste først.</summary>
    public List<Need> Needs { get; init; } = new();

    /// <summary>Alle registrerte ressurser, nyeste først.</summary>
    public List<Resource> Resources { get; init; } = new();

    /// <summary>Antall akutte behov.</summary>
    public int AcuteCount =>
        Needs.Count(need => need.Priority == NeedPriority.Acute);

    /// <summary>Antall planlagte behov.</summary>
    public int PlannedCount =>
        Needs.Count(need => need.Priority == NeedPriority.Planned);

    /// <summary>Antall registrerte ressurser.</summary>
    public int ResourceCount => Resources.Count;

    // Counts explicit statuses only; missing or older free text remains unspecified.
    public int AvailableCount => Resources.Count(resource =>
        string.Equals(resource.Availability?.Trim(), "Tilgjengelig", StringComparison.OrdinalIgnoreCase));

    public int UnavailableCount => Resources.Count(resource =>
        string.Equals(resource.Availability?.Trim(), "Ikke tilgjengelig", StringComparison.OrdinalIgnoreCase));

    public int UnspecifiedCount => ResourceCount - AvailableCount - UnavailableCount;

    // ========================================
    // RANGERING (Bronse, Sølv, Gull, Platina)
    // ========================================
    // Rangeringen regnes ut fra antall registrerte ressurser.
    // Ingenting lagres i databasen. Alt regnes ut hver gang siden vises.

    /// <summary>Poeng (XP) leverandøren får for hver registrerte ressurs.</summary>
    private const int PoengPerRessurs = 100;

    // Nivåene i rekkefølge: navn, nøkkel til CSS (farge på skjoldet),
    // poengene som trengs for å nå nivået, og en tittel som vises i listen.
    private static readonly (string Navn, string Nokkel, int Poeng, string Tittel)[] Nivaer =
    {
        ("Bronse", "bronse", 0, "Ny bidragsyter"),
        ("Sølv", "solv", 200, "Beredskapsbidragsyter"),
        ("Gull", "gull", 500, "Aktiv bidragsyter"),
        ("Platina", "platina", 1000, "Beredskapsambassadør")
    };

    /// <summary>Totale poeng: antall ressurser ganger poeng per ressurs.</summary>
    public int RankPoints => ResourceCount * PoengPerRessurs;

    // Plassen i listen over nivåer: 0 = Bronse, 1 = Sølv, 2 = Gull, 3 = Platina.
    // Vi går gjennom nivåene og husker det høyeste nivået leverandøren har nådd.
    private int RankIndex
    {
        get
        {
            var indeks = 0;

            for (var i = 0; i < Nivaer.Length; i++)
            {
                if (RankPoints >= Nivaer[i].Poeng)
                {
                    indeks = i;
                }
            }

            return indeks;
        }
    }

    /// <summary>Navnet på nivået, for eksempel "Sølv".</summary>
    public string RankName => Nivaer[RankIndex].Navn;

    /// <summary>Nøkkel som styrer fargen på skjoldet i CSS.</summary>
    public string RankKey => Nivaer[RankIndex].Nokkel;

    /// <summary>Sann når leverandøren har nådd det høyeste nivået.</summary>
    public bool IsTopRank => RankIndex == Nivaer.Length - 1;

    /// <summary>Hvor mange poeng som trengs for neste nivå.</summary>
    public int NextRankPoints =>
        IsTopRank ? RankPoints : Nivaer[RankIndex + 1].Poeng;

    /// <summary>Hvor mange ressurser som mangler før neste nivå (rundes opp).</summary>
    public int ResourcesToNextRank =>
        IsTopRank
            ? 0
            : (NextRankPoints - RankPoints + PoengPerRessurs - 1) / PoengPerRessurs;

    /// <summary>Hvor langt poengstolpen er fylt, fra 0 til 100 prosent.</summary>
    public int RankProgressPercent =>
        IsTopRank ? 100 : RankPoints * 100 / NextRankPoints;

    /// <summary>
    /// Alle nivåene, til listen "Slik stiger du i nivå".
    /// Nivået leverandøren er på er merket med IsCurrent.
    /// </summary>
    public List<RankLevelInfo> RankLevels
    {
        get
        {
            var liste = new List<RankLevelInfo>();

            for (var i = 0; i < Nivaer.Length; i++)
            {
                // Hvor mange ressurser som kreves for å nå dette nivået
                var kravRessurser = Nivaer[i].Poeng / PoengPerRessurs;

                liste.Add(new RankLevelInfo
                {
                    Name = Nivaer[i].Navn,
                    Key = Nivaer[i].Nokkel,
                    Title = Nivaer[i].Tittel,
                    Number = i,
                    Description = kravRessurser == 0
                        ? "Her starter alle. Registrer din første ressurs for å komme videre."
                        : "Har registrert minst " + kravRessurser
                          + (kravRessurser == 1 ? " ressurs." : " ressurser."),
                    IsCurrent = i == RankIndex
                });
            }

            return liste;
        }
    }

    // ---- Ferdige tekster til visningen (så Razor-filen slipper logikk) ----

    /// <summary>Tekst ved poengstolpen, for eksempel "240 / 500 XP".</summary>
    public string RankXpText =>
        IsTopRank
            ? RankPoints + " XP"
            : RankPoints + " / " + NextRankPoints + " XP";

    /// <summary>Overskrift i boksen til høyre, for eksempel "Neste nivå: Gull".</summary>
    public string NextRankTitle =>
        IsTopRank
            ? "Høyeste nivå nådd"
            : "Neste nivå: " + Nivaer[RankIndex + 1].Navn;

    /// <summary>Hjelpetekst i boksen til høyre.</summary>
    public string NextRankHint =>
        IsTopRank
            ? "Du har nådd det høyeste nivået. Takk for innsatsen!"
            : "Registrer " + ResourcesToNextRank
              + (ResourcesToNextRank == 1 ? " ressurs" : " ressurser")
              + " til for å nå " + Nivaer[RankIndex + 1].Navn + ".";
}

/// <summary>Ett nivå i rangeringen, brukt til å vise listen over nivåer.</summary>
public class RankLevelInfo
{
    /// <summary>Navnet på nivået, for eksempel "Sølv".</summary>
    public string Name { get; init; } = "";

    /// <summary>Nøkkel som styrer skjold og farge i CSS, for eksempel "solv".</summary>
    public string Key { get; init; } = "";

    /// <summary>Tittel på nivået, for eksempel "Beredskapsbidragsyter".</summary>
    public string Title { get; init; } = "";

    /// <summary>Nivånummer: 0 = Bronse, 1 = Sølv, 2 = Gull, 3 = Platina.</summary>
    public int Number { get; init; }

    /// <summary>Hva som kreves for å nå nivået.</summary>
    public string Description { get; init; } = "";

    /// <summary>Sann hvis dette er nivået leverandøren er på nå.</summary>
    public bool IsCurrent { get; init; }
}
