using System.Text;

namespace FirstWebAppInDocker.Services.Ai;

/// <summary>
/// En hjelpeartikkel Sami kan slå opp i.
/// </summary>
/// <param name="Id">Kort, unik nøkkel.</param>
/// <param name="Title">Overskrift, slik brukeren ville spurt.</param>
/// <param name="Roles">Rollene artikkelen gjelder for.</param>
/// <param name="Keywords">Søkeord, også vanlige skrivemåter og synonymer.</param>
/// <param name="Text">Selve svaret. Skal bare inneholde ting som stemmer med portalen.</param>
public record SamiHelpArticle(
    string Id,
    string Title,
    SamiRole[] Roles,
    string[] Keywords,
    string Text
);


/// <summary>
/// Hjelpesenteret til Sami: korte artikler om alt man kan gjøre i portalen.
///
/// Sami slår opp her med verktøyet search_help i stedet for å gjette.
/// Når portalen endres, oppdaterer man teksten her, så svarer Sami riktig.
/// </summary>
public static class SamiHelpGuide
{
    private static readonly SamiRole[] Everyone =
        { SamiRole.Volunteer, SamiRole.PublicActor, SamiRole.Admin };

    private static readonly SamiRole[] Volunteers =
        { SamiRole.Volunteer };

    private static readonly SamiRole[] Officials =
        { SamiRole.PublicActor, SamiRole.Admin };

    private static readonly SamiRole[] Admins =
        { SamiRole.Admin };


    public static IReadOnlyList<SamiHelpArticle> Articles { get; } = new List<SamiHelpArticle>
    {
        // =====================================================
        // FOR ALLE
        // =====================================================

        new("tema",
            "Mørk modus / bytte tema",
            Everyone,
            new[] { "mørk", "mork", "dark", "mode", "modus", "tema", "theme", "lys", "lyst", "farge", "farger", "natt", "måne", "sol", "bakgrunn", "øynene" },
            """
            Portalen har to temaer: lyseblått (standard) og mørkt marineblått.
            - Trykk på måne-ikonet øverst i menyen, rett ved siden av «Logg ut», for å slå på mørk modus.
            - I mørk modus blir ikonet en sol. Trykk på den for å gå tilbake til lyst tema.
            - Valget huskes i nettleseren, så det gjelder neste gang du åpner portalen på samme maskin og nettleser.
            - Knappen finnes også på forsiden før du logger inn.
            """),

        new("innlogging",
            "Logge inn og ut",
            Everyone,
            new[] { "logge", "logg", "inn", "ut", "innlogging", "login", "logout", "passord", "husk", "låst", "sperret", "utlogget" },
            """
            - Logg inn med e-post og passord på forsiden.
            - Har kontoen tofaktor, får du en sekssifret kode på e-post som du skriver inn etterpå (se «Tofaktor»).
            - Logg ut med knappen «Logg ut» øverst til høyre i menyen.
            - Du blir logget ut automatisk etter omtrent en time uten aktivitet.
            - Etter 5 feil passord blir kontoen låst i 10 minutter. Vent, eller bruk «Glemt passord?».
            - En administrator kan også låse opp en konto.
            """),

        new("glemt-passord",
            "Glemt eller bytte passord",
            Everyone,
            new[] { "glemt", "passord", "bytte", "endre", "nytt", "tilbakestille", "reset", "password" },
            """
            - Trykk «Glemt passord?» på innloggingssiden og skriv inn e-posten din.
            - Du får en e-post med en lenke der du lager et nytt passord. Passordet må ha minst 8 tegn.
            - Det finnes ikke egen knapp for å bytte passord på profilsiden. Vil du bytte passord, logger du ut og bruker «Glemt passord?».
            - Lokalt i utviklingsmiljøet havner e-postene i Mailpit på http://localhost:8025.
            """),

        new("opprett-bruker",
            "Opprette en ny bruker",
            Everyone,
            new[] { "opprette", "opprett", "ny", "bruker", "konto", "registrere", "meg", "signup", "melde" },
            """
            - Trykk «Opprett bruker» på forsiden.
            - Fyll inn navn, e-post, telefonnummer (valgfritt) og passord på minst 8 tegn.
            - Du får en e-post med en bekreftelseslenke. Kontoen kan brukes først når e-posten er bekreftet.
            - Nye brukere blir ressursleverandører (frivillige) og kan registrere ressurser.
            - Offentlige aktører (kommune, politi, Heimevernet osv.) kan ikke registrere seg selv. De får konto av en administrator.
            """),

        new("tofaktor",
            "Tofaktor / sikkerhetskode på e-post",
            Everyone,
            new[] { "tofaktor", "2fa", "to-faktor", "kode", "sikkerhetskode", "engangskode", "sikkerhet", "bekreftelse", "sekssifret" },
            """
            - Tofaktor betyr at du må skrive inn en sekssifret kode som sendes til e-posten din hver gang du logger inn.
            - Den slås på automatisk når en ny bruker bekrefter e-postadressen sin. Du trenger ingen app.
            - Fikk du ikke koden, trykk «Send ny kode på e-post» på kodesiden. Sjekk også søppelpost.
            - På profilsiden ser du om e-posten er bekreftet og om tofaktor er aktivert. Tofaktor kan ikke slås av derfra.
            """),

        new("profil",
            "Endre navn, e-post eller telefonnummer (profil)",
            Everyone,
            new[] { "profil", "endre", "navn", "e-post", "epost", "email", "telefon", "telefonnummer", "mobil", "kontaktinfo", "konto", "innstillinger" },
            """
            - Trykk «Profil» i menyen.
            - Der kan du endre navn, e-post og telefonnummer (landkode +47, +45 eller +46).
            - Endrer du e-post, må den nye adressen bekreftes via en lenke på e-post før den tas i bruk.
            - Profilsiden viser også om e-posten er bekreftet og om tofaktor er aktivert.
            """),

        new("varsler",
            "Varsler og e-post",
            Everyone,
            new[] { "varsel", "varsler", "klokke", "bjelle", "notifikasjon", "melding", "meldinger", "e-post", "sms", "mailpit" },
            """
            - Klokka øverst i menyen viser varsler. Et rødt tall betyr nye varsler.
            - Trykk på klokka for å se alle varsler. De markeres som lest når du åpner listen.
            - Du får varsel når noen ønsker ressursen din, når noen svarer på en forespørsel, og når et behov er løst.
            - De samme varslene sendes på e-post. Lokalt havner e-postene i Mailpit på http://localhost:8025. SMS er simulert i prototypen.
            """),

        new("kart",
            "Kartet: zoome og bytte karttype",
            Everyone,
            new[] { "kart", "karttype", "sjøkart", "topografisk", "zoom", "zoome", "vanlig", "kartverket", "satellitt", "flytte" },
            """
            - Øverst til høyre i kartene er det en meny der du velger «Vanlig kart», «Topografisk kart» eller «Sjøkart».
            - Zoom med + og − i hjørnet, med musehjulet eller med to fingre på mobil. Dra i kartet for å flytte det.
            - Kartbildene kommer fra Kartverket og OpenStreetMap.
            """),

        new("roller",
            "Hvilke roller finnes, og hva kan de gjøre?",
            Everyone,
            new[] { "rolle", "roller", "tilgang", "frivillig", "ressursleverandør", "offentlig", "aktør", "admin", "administrator", "hvem" },
            """
            - Ressursleverandør / frivillig: registrerer egne ressurser, svarer på forespørsler og ser behov de kan hjelpe med. Ser bare sine egne ressurser.
            - Offentlig aktør (kommune, politi, brann, helse, Sivilforsvaret, Heimevernet): registrerer behov, ser alle ressurser og kobler ressurser til behov.
            - Administrator: kan alt en offentlig aktør kan, og administrerer brukere.
            """),

        new("sami",
            "Hva kan Sami hjelpe med?",
            Everyone,
            new[] { "sami", "assistent", "chat", "bot", "ai", "hjelp", "spørre", "kan du" },
            """
            - Sami svarer på spørsmål om hvordan portalen fungerer og viser vei til riktig side.
            - Frivillige kan be Sami registrere en ressurs: Sami lager et utkast, og ressursen lagres først når du trykker «Registrer ressursen».
            - Offentlige aktører kan beskrive en hendelse, så foreslår Sami ressurser i nærheten. Forespørsler sendes bare når du trykker på knappen.
            - Sami er en AI og kan ta feil. Sjekk viktige opplysninger selv.
            - Ved fare for liv og helse: ring 112 (politi), 110 (brann) eller 113 (ambulanse).
            - Trykk på pila i ring øverst i chatten for å starte en ny samtale.
            """),

        // =====================================================
        // FRIVILLIG / RESSURSLEVERANDØR
        // =====================================================

        new("niva",
            "Nivå og poeng: hvordan går jeg opp i nivå?",
            Volunteers,
            new[] { "nivå", "niva", "level", "poeng", "xp", "rang", "rangering", "bronse", "sølv", "solv", "gull", "platina", "skjold", "opp", "stige", "belønning" },
            """
            - Du får 100 poeng (XP) for hver ressurs du har registrert. Jo flere ressurser, jo høyere nivå.
            - Nivåene er:
              - Bronse (Ny bidragsyter): 0 poeng, der alle starter
              - Sølv (Beredskapsbidragsyter): 200 poeng = 2 ressurser
              - Gull (Aktiv bidragsyter): 500 poeng = 5 ressurser
              - Platina (Beredskapsambassadør): 1000 poeng = 10 ressurser
            - Nivået ditt, skjoldet og poengstolpen vises på «Oversikt». Under «Slik stiger du i nivå» ser du alle nivåene.
            - Poengene regnes ut fra hvor mange ressurser du har registrert nå.
            """),

        new("registrer-ressurs",
            "Registrere en ressurs",
            Volunteers,
            new[] { "registrere", "registrer", "legge", "til", "ny", "ressurs", "tilby", "skjema", "adresse", "plassering", "periode" },
            """
            - Trykk «Registrer ressurs» i menyen. Du kan også be Sami om å registrere den for deg.
            - Feltene er:
              - Ressurstype (påkrevd)
              - Navn, maks 100 tegn (påkrevd)
              - Beskrivelse, maks 500 tegn (påkrevd)
              - Antall, minst 1 (påkrevd)
              - Kapasitet, f.eks. «8 personer» (valgfritt)
              - Hvor lenge ressursen skal være tilgjengelig: 1, 3 eller 6 måneder, eller 1, 2 eller 3 år (påkrevd)
              - Adresse: skriv i adressefeltet og velg et forslag fra Kartverket, eller klikk i kartet (plassering er påkrevd)
              - Kontakt-e-post (står den tom, brukes e-posten på kontoen) og telefonnummer (valgfritt; +47 og +45 har 8 sifre, +46 har 7–10)
            - Når ressursen er lagret, får du 100 poeng og kommer til «Oversikt».
            """),

        new("mine-ressurser",
            "Se, endre eller slette mine ressurser",
            Volunteers,
            new[] { "mine", "ressurser", "endre", "slette", "fjerne", "redigere", "oversikt", "status", "utilgjengelig", "tilgjengelig", "pause", "utløpt", "periode" },
            """
            - «Mine ressurser» i menyen viser ressursene du har registrert. Du ser aldri andres ressurser.
            - Trykk «Marker som utilgjengelig» hvis ressursen ikke kan brukes en stund, og «Marker som tilgjengelig» når den kan brukes igjen.
            - «Opptatt» betyr at ressursen er bekreftet til et behov. Den blir ledig igjen når behovet er løst.
            - Når tilgjengelighetsperioden er over, regnes ressursen som utilgjengelig.
            - Det finnes ikke knapp for å endre eller slette en ressurs selv. Kontakt en administrator, eller registrer den på nytt med riktige opplysninger.
            """),

        new("foresporsler",
            "Forespørsler: når noen ønsker ressursen min",
            Volunteers,
            new[] { "forespørsel", "foresporsel", "forespørsler", "ønsket", "bekrefte", "bekreft", "avslå", "kan ikke", "svare", "tildelt", "stille" },
            """
            - Når en offentlig aktør trenger ressursen din, får du varsel (klokka og e-post).
            - Gå til «Forespørsler» i menyen. Trykk «Bekreft at jeg kan stille» eller «Kan ikke».
            - Bekreft bare hvis du faktisk kan stille. Ressursen blir da «Opptatt» til behovet er løst.
            - Etter at du har bekreftet, ser du kontaktinformasjonen til den offentlige aktøren under «Tidligere forespørsler».
            """),

        new("behov-jeg-kan-hjelpe-med",
            "Se behov jeg kan hjelpe med",
            Volunteers,
            new[] { "behov", "hjelpe", "bidra", "trenger", "nær", "nærheten", "jeg kan hjelpe", "tilby" },
            """
            - «Behov jeg kan hjelpe med» i menyen viser åpne behov som passer ressurstypene dine.
            - Du ser bare omtrentlig avstand, ikke nøyaktig sted eller hvem som har meldt behovet.
            - Velg søkeradius øverst (10–200 km).
            - Trykk «Jeg kan hjelpe», så får den offentlige aktøren beskjed og kan sende deg en forespørsel.
            """),

        // =====================================================
        // OFFENTLIG AKTØR / ADMIN
        // =====================================================

        new("registrer-behov",
            "Registrere et behov",
            Officials,
            new[] { "registrere", "registrer", "behov", "melde", "nytt", "trenger", "prioritet", "akutt", "planlagt" },
            """
            - Trykk «Registrer behov» i menyen.
            - For hvert behov velger du type, prioritet (Akutt eller Planlagt), antall (1–9999), skriver en beskrivelse (maks 500 tegn) og klikker i kartet der behovet er.
            - Du kan registrere flere behov i samme skjema.
            """),

        new("behov-oversikt",
            "Se, endre eller slette behov",
            Officials,
            new[] { "behov", "oversikt", "endre", "slette", "redigere", "status", "liste" },
            """
            - «Behov» i menyen viser alle registrerte behov, nyeste først, med status (Ny, Vurderes, Tildelt eller Løst).
            - Behov du selv har registrert kan du endre eller slette med knappene på behovet.
            - «Finn ressurser» på et behov åpner siden der du finner og spør ressurser i nærheten.
            """),

        new("koordinering",
            "Koordinering: finne ressurser og sende forespørsler",
            Officials,
            new[] { "koordinering", "matche", "matching", "koble", "finn", "ressurser", "forespørsel", "sende", "tavle", "historikk", "tildeling", "løst", "avbryt" },
            """
            - «Koordinering» i menyen gir oversikt over uløste behov, forespørsler som venter på svar og bekreftede ressurser.
            - Fanene er «Tavle» (behovene etter status), «Forespørsler» og «Historikk».
            - Åpne et behov og trykk «Finn ressurser». Du ser tilgjengelige ressurser av riktig type, sortert etter avstand. Velg radius øverst.
            - «Send forespørsel» varsler eieren, som må bekrefte. Behovet blir da «Vurderes», og «Tildelt» når noen har bekreftet.
            - Du kan avbryte en forespørsel og trykke «Marker behovet som løst» når behovet er dekket. Bekreftede ressurser blir da ledige igjen.
            - Sami kan også foreslå ressurser hvis du beskriver hendelsen.
            """),

        new("statusflyt",
            "Hva betyr statusene Ny, Vurderes, Tildelt og Løst?",
            Officials,
            new[] { "status", "ny", "vurderes", "tildelt", "løst", "opptatt", "venter", "betyr" },
            """
            - Ny: ingen er spurt ennå.
            - Vurderes: minst én ressurs er spurt, og det venter på svar.
            - Tildelt: minst én eier har bekreftet at ressursen stiller.
            - Løst: behovet er markert som dekket. Bekreftede ressurser blir tilgjengelige igjen.
            """),

        new("ressursoversikt",
            "Se og filtrere alle ressurser",
            Officials,
            new[] { "ressurser", "oversikt", "filter", "filtrere", "søke", "søk", "kontakt", "telefon", "nullstill", "kapasitet" },
            """
            - «Ressurser» i menyen viser alle registrerte ressurser med kontaktinformasjon og plassering i kart.
            - Filtrer på ressurstype og tilgjengelighet, eller søk etter navn. «Nullstill filtre» viser alt igjen.
            """),

        new("admin-brukere",
            "Administrere brukere (admin)",
            Admins,
            new[] { "admin", "brukere", "bruker", "rolle", "endre rolle", "deaktivere", "deaktiver", "reaktivere", "låse opp", "slette", "opprette", "offentlig aktør" },
            """
            - «Brukere» i menyen viser alle kontoer med rolle og status.
            - Endre rolle med nedtrekksmenyen på brukeren. Brukeren må logge inn på nytt for at den nye rollen skal gjelde.
            - «Deaktiver» hindrer brukeren i å logge inn. Brukere slettes ikke, slik at historikken beholdes. «Reaktiver» eller «Lås opp» gir tilgang igjen.
            - «Opprett offentlig aktør» lager konto for en kommune eller etat. Gi personen det midlertidige passordet på en trygg måte.
            - Du kan se ressursene til en bruker og slette ressurser som er feilregistrert.
            - Du kan ikke endre din egen rolle eller deaktivere deg selv.
            """),
    };


    /// <summary>
    /// Finner de artiklene som passer best til spørsmålet, for brukerens rolle.
    /// Enkel nøkkelordsøk: treff i søkeord teller mest, deretter treff i tittel og tekst.
    /// </summary>
    public static List<SamiHelpArticle> Search(string? query, SamiRole role, int maxResults = 3)
    {
        List<SamiHelpArticle> available =
            Articles.Where(article => article.Roles.Contains(role)).ToList();

        string[] words = Words(query);

        if (words.Length == 0)
        {
            return new List<SamiHelpArticle>();
        }

        return available
            .Select(article => (Article: article, Score: Score(article, words)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Take(maxResults)
            .Select(item => item.Article)
            .ToList();
    }


    /// <summary>
    /// Titlene på alle artiklene brukeren har tilgang til.
    /// </summary>
    public static List<string> TopicsFor(SamiRole role)
    {
        return Articles
            .Where(article => article.Roles.Contains(role))
            .Select(article => article.Title)
            .ToList();
    }


    private static int Score(SamiHelpArticle article, string[] words)
    {
        string title = Normalize(article.Title);
        string text = Normalize(article.Text);
        string[] keywords = article.Keywords.Select(Normalize).ToArray();

        int score = 0;

        foreach (string word in words)
        {
            if (keywords.Any(keyword => keyword == word))
            {
                score += 5;
            }
            else if (word.Length >= 4 && keywords.Any(keyword => keyword.StartsWith(word) || word.StartsWith(keyword)))
            {
                score += 3;
            }

            if (title.Contains(word))
            {
                score += 2;
            }
            else if (word.Length >= 4 && text.Contains(word))
            {
                score += 1;
            }
        }

        return score;
    }


    // Små ord som "jeg", "hvordan" og "på" gir ikke treff, så de hoppes over.
    private static readonly HashSet<string> StopWords = new()
    {
        "jeg", "du", "deg", "meg", "min", "mitt", "mine", "hvordan", "hva", "hvor", "når", "kan", "skal", "vil",
        "går", "gjør", "gjøre", "får", "på", "i", "til", "av", "en", "et", "ei", "det", "den", "de", "er", "og",
        "eller", "for", "med", "om", "som", "hei", "sami", "the", "how", "do", "is", "a", "an", "to", "my",
        "noe", "noen", "her", "der", "ikke", "bare", "også", "fra"
    };


    private static string[] Words(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        return Normalize(query)
            .Split(new[] { ' ', ',', '.', '?', '!', ':', ';', '«', '»', '"', '\'', '(', ')', '/' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 1 && !StopWords.Contains(word))
            .Distinct()
            .ToArray();
    }


    private static string Normalize(string value)
    {
        // ToLowerInvariant gjør også Æ, Ø og Å om til små bokstaver.
        return value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
