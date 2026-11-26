/*
    DashboardViewModel.cs

    Hva filen gjør: Det dashbordet viser: behov, ressurser og nivå/poeng for frivillige.

    Oppgavekrav: 18
*/

using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/*
    Data som vises på dashboardet til offentlig aktør.
    Hentes fra databasen av HomeController.
*/
public class DashboardViewModel
{
    // Alle registrerte behov, nyeste først.
    public List<Need> Needs { get; init; } = new();

    // Alle registrerte ressurser, nyeste først.
    public List<Resource> Resources { get; init; } = new();

    // Antall akutte behov.
    public int AcuteCount => Needs.Count(need => need.Priority == NeedPriority.Acute);

    // Antall planlagte behov.
    public int PlannedCount => Needs.Count(need => need.Priority == NeedPriority.Planned);

    // Antall registrerte ressurser.
    public int ResourceCount => Resources.Count;

    /*
        Counts explicit statuses only; missing or older free text remains unspecified.
        Nye perioder bruker status/sluttdato; eldre registreringer beholder eksisterende tolkning.
    */
    public int AvailableCount => Resources.Count(resource =>
        string.Equals(Services.ResourceAvailability.DashboardStatus(resource)?.Trim(), "Tilgjengelig", StringComparison.OrdinalIgnoreCase));

    public int UnavailableCount => Resources.Count(resource =>
        string.Equals(Services.ResourceAvailability.DashboardStatus(resource)?.Trim(), "Ikke tilgjengelig", StringComparison.OrdinalIgnoreCase));

    public int UnspecifiedCount => ResourceCount - AvailableCount - UnavailableCount;

    /*
        RANGERING (Bronse, Sølv, Gull, Platina)
        Rangeringen regnes ut fra antall registrerte ressurser.
        Ingenting lagres i databasen. Alt regnes ut hver gang siden vises.
    */

    // Poeng (XP) leverandøren får for hver registrerte ressurs.
    private const int PoengPerRessurs = 100;

    /*
        Nivåene i rekkefølge: navn, nøkkel til CSS (farge på skjoldet),
        poengene som trengs for å nå nivået, og en tittel som vises i listen.
    */
    private static readonly (string Navn, string Nokkel, int Poeng, string Tittel)[] Nivaer =
    {
        ("Bronse", "bronse", 0, "Ny bidragsyter"),
        ("Sølv", "solv", 200, "Beredskapsbidragsyter"),
        ("Gull", "gull", 500, "Aktiv bidragsyter"),
        ("Platina", "platina", 1000, "Beredskapsambassadør")
    };

    // Totale poeng: antall ressurser ganger poeng per ressurs.
    public int RankPoints => ResourceCount * PoengPerRessurs;

    /*
        Plassen i listen over nivåer: 0 = Bronse, 1 = Sølv, 2 = Gull, 3 = Platina.
        Vi går gjennom nivåene og husker det høyeste nivået leverandøren har nådd.
    */
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

    // Navnet på nivået, for eksempel "Sølv".
    public string RankName => Nivaer[RankIndex].Navn;

    // Nøkkel som styrer fargen på skjoldet i CSS.
    public string RankKey => Nivaer[RankIndex].Nokkel;

    // Sann når leverandøren har nådd det høyeste nivået.
    public bool IsTopRank => RankIndex == Nivaer.Length - 1;

    // Hvor mange poeng som trengs for neste nivå.
    public int NextRankPoints => IsTopRank ? RankPoints : Nivaer[RankIndex + 1].Poeng;

    // Hvor mange ressurser som mangler før neste nivå (rundes opp).
    public int ResourcesToNextRank =>
        IsTopRank
            ? 0
            : (NextRankPoints - RankPoints + PoengPerRessurs - 1) / PoengPerRessurs;

    // Hvor langt poengstolpen er fylt, fra 0 til 100 prosent.
    public int RankProgressPercent => IsTopRank ? 100 : RankPoints * 100 / NextRankPoints;

    /*
        Alle nivåene, til listen "Slik stiger du i nivå".
        Nivået leverandøren er på er merket med IsCurrent.
    */
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

    // Tekst ved poengstolpen, for eksempel "240 / 500 poeng".
    public string RankXpText =>
        IsTopRank
            ? RankPoints + " poeng"
            : RankPoints + " / " + NextRankPoints + " poeng";

    // Overskrift i boksen til høyre, for eksempel "Neste nivå: Gull".
    public string NextRankTitle =>
        IsTopRank
            ? "Høyeste nivå nådd"
            : "Neste nivå: " + Nivaer[RankIndex + 1].Navn;

    // Hjelpetekst i boksen til høyre.
    public string NextRankHint =>
        IsTopRank
            ? "Du har nådd det høyeste nivået. Takk for innsatsen!"
            : "Registrer " + ResourcesToNextRank
              + (ResourcesToNextRank == 1 ? " ressurs" : " ressurser")
              + " til for å nå " + Nivaer[RankIndex + 1].Navn + ".";
}

// Ett nivå i rangeringen, brukt til å vise listen over nivåer.
public class RankLevelInfo
{
    // Navnet på nivået, for eksempel "Sølv".
    public string Name { get; init; } = "";

    // Nøkkel som styrer skjold og farge i CSS, for eksempel "solv".
    public string Key { get; init; } = "";

    // Tittel på nivået, for eksempel "Beredskapsbidragsyter".
    public string Title { get; init; } = "";

    // Nivånummer: 0 = Bronse, 1 = Sølv, 2 = Gull, 3 = Platina.
    public int Number { get; init; }

    // Hva som kreves for å nå nivået.
    public string Description { get; init; } = "";

    // Sann hvis dette er nivået leverandøren er på nå.
    public bool IsCurrent { get; init; }
}
