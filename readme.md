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

Det opprettes ikke automatisk en standard administratorkonto.

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

# KI-redegjørelse

KI har vært et verdifullt hjelpemiddel som gruppen har benyttet gjennom prosjektarbeidet. Gruppen har hovedsakelig brukt KI til å forstå nye fagbegreper innen programmering, utvikle ideer, foreslå forbedringer i kode og identifisere feil når applikasjonen ikke har fungert som forventet.

Verktøy som Microsoft Copilot og Claude.ai har blitt brukt som støtte i arbeidet.

KI har bidratt til å styrke gruppens faglige kompetanse og forståelse av hvordan kunstig intelligens kan brukes som et hjelpemiddel i prosjektarbeid og teknologiutvikling. Gruppen har samtidig hatt fokus på å bruke KI på en ansvarlig måte, der forslag og løsninger vurderes og kontrolleres før de tas i bruk.

## Kilder

Microsoft. (2026). *Copilot* [AI-assistent]. https://copilot.microsoft.com

Anthropic. (2026). *Claude* [AI-assistent]. https://claude.ai
