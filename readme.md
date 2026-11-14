# Beredskapsportalen

Beredskapsportalen er en webapplikasjon utviklet som en del av et akademisk prosjekt, støttet av Heimevernet og Kartverket. Løsningen er bygget med ASP.NET Core MVC og bruker MariaDB som database. Entity Framework Core benyttes for kommunikasjon mellom applikasjonen og databasen. Docker brukes for å starte applikasjonen og databasen lokalt.

# Teknologistack

Prosjektet bygger på følgende teknologier:

- ASP.NET Core MVC
- .NET 10
- MariaDB 11.4
- Entity Framework Core
- ASP.NET Core Identity
- Docker og Docker Compose
- xUnit (må bekreftes mot testprosjektet)

# Prosjektstruktur

Løsningen består av to .NET-prosjekter med ulike ansvarsområder.

## FirstWebAppInDocker

Hovedapplikasjonen som brukerne møter. Her finnes blant annet:

- Controllers – håndterer forespørsler og funksjonalitet.
- Views – inneholder brukergrensesnittet.
- Models – inneholder datamodellene.
- ViewModels – modeller som brukes til å overføre data mellom kontrollere og visninger.
- Data – inneholder databasekontekst og Identity-oppsett.
- Migrations – inneholder Entity Framework Core-migreringer.
- wwwroot – inneholder CSS, JavaScript og andre statiske filer.
- Autentisering og autorisering.
- Tilgang til databasen.

## FirstWebAppInDocker.Tests

Inneholder automatiserte tester for hovedapplikasjonen.

## Docker-konfigurasjon

Prosjektet inneholder også følgende filer for lokal kjøring:

- `Dockerfile` – beskriver hvordan applikasjonscontaineren bygges.
- `docker-compose.yml` – konfigurerer og starter applikasjonen og MariaDB.
- `.env.example` – eksempel på lokale miljøvariabler.
- `.env` – inneholder lokale miljøvariabler og eventuelle passord.

# Krav

Følgende programvare må være installert før prosjektet kan kjøres:

- Git
- .NET SDK 10
- Docker Desktop
- Visual Studio Code eller Visual Studio

MariaDB kjøres i en Docker-container. Docker Desktop må derfor være startet før applikasjonen kjøres.

# Kontroller .NET-installasjonen

For å kontrollere .NET-installasjonen benyttes følgende kommando i PowerShell-terminalen:

```powershell
dotnet --version
```

# Kjøre prosjektet lokalt med Docker

## 1. Kloning av repository

Repositoryet klones med følgende kommandoer:

```powershell
git clone https://github.com/sueromartinez/IS-202-innlevering.git
cd IS-202-innlevering
```

## 2. Opprettelse av lokale miljøvariabler

Eksempelfilen kopieres med følgende kommando:

```powershell
Copy-Item .env.example .env
```

I `.env` kan gruppemedlemmene konfigurere egne lokale passord dersom standardverdiene skal endres.

Passord og andre sensitive opplysninger skal ikke legges direkte inn i Git-repositoryet.

## 3. Oppstart av prosjektet

Fra rotmappen i prosjektet startes applikasjonen med:

```powershell
docker compose up --build
```

Docker Compose starter først MariaDB-databasen. Når databasen er klar, starter applikasjonen.

Applikasjonen er tilgjengelig på:

http://localhost:8080

Databasen heter `beredskap_db` og kjører i en MariaDB 11.4-container.

## Stoppe applikasjonen

Containerne stoppes med følgende kommando:

```powershell
docker compose down
```

Databasevolumet beholdes når containerne stoppes. Det betyr at lagrede data ikke slettes ved vanlig nedstenging.

# Database og datatilgang

Prosjektet bruker MariaDB som databasesystem og Entity Framework Core (EF Core) for datatilgang.

Databaseforbindelsen konfigureres gjennom connection string `DefaultConnection`.

Når applikasjonen starter, kjøres ventende EF Core-migreringer automatisk gjennom:

```csharp
await dbContext.Database.MigrateAsync();
```

Dette innebærer at:

- Databasestrukturen opprettes automatisk dersom databasen er ny.
- Nye migreringer kjøres automatisk ved oppstart.
- Allerede gjennomførte migreringer kjøres ikke på nytt.

Docker Compose konfigurerer databaseforbindelsen mellom applikasjonen og MariaDB.

# Innlogging og brukerhåndtering

Løsningen bruker ASP.NET Core Identity for autentisering og brukerhåndtering.

Følgende roller opprettes automatisk ved oppstart:

- Admin
- PublicActor
- ResourceProvider

## Standardbruker

Ved oppstart opprettes en offentlig testbruker automatisk dersom brukeren ikke allerede finnes.

| Felt | Verdi |
|---|---|
| E-post | offentlig@beredskap.no |
| Passord | Test123! |
| Navn | Beredskapsbruker |
| Rolle | PublicActor |

Testbrukeren opprettes gjennom `IdentitySeeder.cs`.

Passordet er ment for testing og utvikling, og skal ikke brukes i et produksjonsmiljø.

En administratorkonto opprettes også ved oppstart (e-post og passord kan settes i `.env`). Se «Testbrukere» under «Roller».

# Opprette bruker

Nye brukere kan registrere seg gjennom registreringsfunksjonen i applikasjonen.

1. Applikasjonen åpnes.
2. Registreringsfunksjonen velges.
3. Nødvendig informasjon fylles inn.
4. Brukeren opprettes.

Nye brukere registreres som ressursleverandører.

# Testing

## Kjøre alle tester

Alle automatiserte tester kjøres fra rotmappen med følgende kommando:

```powershell
dotnet test .\FirstWebAppInDocker.Tests\FirstWebAppInDocker.Tests.csproj
```

## Bygge hovedapplikasjonen

Hovedapplikasjonen bygges med følgende kommando:

```powershell
dotnet build .\FirstWebAppInDocker.csproj
```

Prosjektet har ikke en egen `.sln`- eller `.slnx`-fil. Derfor benyttes de faktiske `.csproj`-filene i kommandoene.

# Arbeidsflyt

Alle gruppemedlemmene utvikler kode på egne Git-branches før endringene pushes og foreslås integrert i `main` gjennom en Pull Request.

Før koden pushes bør gruppemedlemmene:

1. Kjøre relevante tester.
2. Bygge prosjektet uten feil.
3. Gjennomføre en enkel egenkontroll.
4. Kontrollere at endringene ikke påvirker eksisterende funksjonalitet.
5. Opprette en Pull Request for gjennomgang og integrering i `main`.

# Roller

| Rolle | Kan gjøre |
|---|---|
| Ressursleverandør / frivillig | Registrere og endre egne ressurser, se forespørsler og bekrefte/avslå, se behov de kan hjelpe med |
| Offentlig aktør | Registrere behov, se alle ressurser, matche ressurser med behov, markere behov som løst |
| Admin | Alt en offentlig aktør kan, pluss å opprette offentlige aktører, endre roller og deaktivere brukere |

Offentlige aktører kan ikke registrere seg selv. De opprettes av admin under **Brukere**.
Brukere deaktiveres i stedet for å slettes, slik at historikken beholdes.

## Testbrukere (bare lokalt)

| Rolle | E-post | Passord |
|---|---|---|
| Admin | admin@beredskap.no | Admin123! (kan endres i `.env`) |
| Offentlig aktør | offentlig@beredskap.no | Test123! |
| Frivillig | frivillig@beredskap.no | Test123! |
| Bedrift | entreprenor@beredskap.no | Test123! |

Frivillig- og bedriftsbrukeren, ressursene og behovene er demodata som opprettes når databasen er tom
(kun i Development). Slå av med miljøvariabelen `Seed__DemoData=false`.

---

# Matching og statusflyt

1. Offentlig aktør trykker **Finn ressurser** på et behov. Systemet viser tilgjengelige ressurser sortert etter avstand (Haversine).
2. **Send forespørsel** → behovet får status *Vurderes*, og eieren av ressursen får varsel i portalen (klokken), på e-post og simulert SMS.
3. Eieren bekrefter under **Forespørsler** → behovet blir *Tildelt*, ressursen *Opptatt*.
4. **Marker behovet som løst** → behovet blir *Løst*, ressursen blir tilgjengelig igjen.

E-post fanges av **Mailpit** og kan leses på http://localhost:8025. SMS logges bare i konsollen.

## Frist, kontaktpunkt og hastegrad

Et behov kan få **frist** og **kontaktpunkt** (kontaktperson og telefon). Kontaktpunktet vises til den frivillige først etter at hen har bekreftet.

Hvert åpent behov får en **hastegrad** fra 0 til 100 (`Services/Matching/NeedUrgencyService.cs`). Modellen er bevisst enkel og forklarbar:

| Faktor | Poeng |
|---|---|
| Prioritet | Akutt 50, Planlagt 15 |
| Frist | passert +35, innen 1 døgn +30, innen 3 døgn +20, innen 7 døgn +10 |
| Ventetid (ikke tildelt) | over 24 t +15, over 6 t +8, over 1 t +3 |
| Dekning | ingen ressurser av riktig type innen 50 km +20, bare 1–2 +8 |

Tildelte behov får 30 % av poengene, løste behov 0. Nivå: 70+ Kritisk, 45–69 Høy, 25–44 Middels, ellers Lav. Grunnene vises når man holder musen over merket.

## Frivillige: endre og slette egne ressurser

Under **Mine ressurser** har hver ressurs knappene **Endre** og **Slett**. En ressurs som er *Opptatt* eller har forespørsler knyttet til seg kan ikke slettes, slik at historikken beholdes (samme regel som for admin).

---

# Rapporter, eksport og sporbarhet

**Rapporter** i menyen (offentlig aktør og admin):

- **Situasjonsrapport** – nøkkeltall, dekning per ressurstype (mangler og knapp dekning), ressurser per kommune, de mest presserende behovene og behov uten ressurser innen 50 km. Tallene regnes ut i C# (`Services/Reports/ReportService.cs`), og en regelbasert oppsummering virker uten AI. Knappen **Be Sami skrive vurdering** sender bare tallene (ingen navn, e-post eller telefon) til språkmodellen og får tilbake situasjonsbilde, risikoer og anbefalte tiltak.
- **Ressursliste per kommune** – utskriftsvennlig liste med filter på kommune, type og tilgjengelighet. **Skriv ut** skjuler meny og knapper.
- **Eksport** – samme liste som **CSV** (semikolon og UTF-8 med BOM, åpnes riktig i norsk Excel, beskyttet mot formel-injeksjon) eller **JSON** for andre systemer: `/Report/Export?format=csv&kommune=Kristiansand`.

**Aktivitetslogg** (admin, `/Admin/AuditLog`): innlogginger og mislykkede innlogginger, utlogging, endringer på ressurser og behov, forespørsler og svar, løste behov, rolleendringer, nye/deaktiverte brukere, eksport og situasjonsrapporter. Filtrer på handling, periode og tekst. Loggingen skjer i `Services/AuditLog.cs`; feiler den, stopper den aldri selve handlingen.

**Flybilder:** kartmenyen har også **Flybilder** fra Norge i bilder (Kartverket/Geovekst).

---

# Sami – AI-assistenten

Sami er hjelpeboten nede i høyre hjørne på alle sider etter innlogging.

- **Frivillig:** fortell Sami hva du vil tilby og hvor det står, så lager Sami et utkast til ressursen. Ressursen lagres først når du trykker **Registrer ressursen** (samme skjema og validering som «Registrer ressurs»). Sami kan også si hvilket nivå du er på og hva som skal til for neste.
- **Offentlig aktør:** beskriv en hendelse («flom i Grimstad sentrum»), så finner Sami ressurser i nærheten og foreslår matcher. Forslag vises som kort med knappen **Send forespørsel**; Sami lagrer aldri noe selv.
- **Admin:** det samme, pluss tall for hele portalen.

Alle roller kan spørre Sami om hvordan portalen fungerer (mørk modus, nivå, innlogging, tofaktor, profil, varsler osv.). Svarene hentes fra hjelpeartiklene i `Services/Ai/SamiHelpGuide.cs`. **Endrer dere en funksjon i portalen, oppdater artikkelen der**, så svarer Sami riktig.

Avstand og filtrering beregnes i C# (`ResourceMatchingService`). Språkmodellen får ferdige kandidater, uten kontaktinformasjon, og forklarer og rangerer dem.

## Språkmodell

Standard er en **lokal modell via Ollama** (gratis, ingen API-nøkkel, ingen data forlater maskinen).
`docker compose up` starter Ollama og laster ned modellen `qwen2.5:7b` (ca. 4–5 GB) første gang.
Følg med på nedlastingen med:

    docker compose logs -f ollama-pull

Svar kan ta noen sekunder på en laptop uten skjermkort.

### Bytte til Claude API (valgfritt)

Legg dette i `.env` (aldri commit nøkkelen):

    SAMI_PROVIDER=Claude
    ANTHROPIC_API_KEY=sk-ant-...

---

# Etter at denne endringen er hentet

Datamodellen har fått nye tabeller og felt (frist og kontaktpunkt på behov, og tabellen `AuditLog`).
Lag migrasjonen én gang før appen startes:

    dotnet tool install --global dotnet-ef   (hvis du ikke har det)
    dotnet ef migrations add JuryForbedringer
    dotnet build
    dotnet test FirstWebAppInDocker.Tests
    docker compose up --build

Migrasjonen kjøres automatisk når appen starter.

---

# KI-redegjørelse

KI har vært et verdifullt hjelpemiddel som gruppen har benyttet gjennom prosjektarbeidet. Gruppen har hovedsakelig brukt KI til å forstå nye fagbegreper innen programmering, utvikle ideer, foreslå forbedringer i kode og identifisere feil når applikasjonen ikke har fungert som forventet.

Verktøy som Microsoft Copilot og Claude.ai har blitt brukt som støtte i arbeidet.

KI har bidratt til å styrke gruppens faglige kompetanse og forståelse av hvordan kunstig intelligens kan brukes som et hjelpemiddel i prosjektarbeid og teknologiutvikling. Gruppen har samtidig hatt fokus på å bruke KI på en ansvarlig måte, der forslag og løsninger vurderes og kontrolleres før de tas i bruk.

## Kilder

Microsoft. (2026). *Copilot* [AI-assistent]. https://copilot.microsoft.com

Anthropic. (2026). *Claude* [AI-assistent]. https://claude.ai
