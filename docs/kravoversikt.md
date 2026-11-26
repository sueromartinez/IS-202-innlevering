# Kravoversikt

Hvor hvert krav i oppgaven er oppfylt. Kravnumrene står også øverst i kodefilene («Oppgavekrav: …»).

| # | Krav | Hvor |
|---|---|---|
| 1 | .NET MVC Core + MariaDB, kjøres i Docker (Compose) | `docker-compose.yml`, `Dockerfile`, `Program.cs` |
| 2 | Dokumentasjon i koden og readme/wiki | Forklaring øverst i hver fil, `readme.md`, `docs/` |
| 3 | Konfigurering for sensor | [drift.md](drift.md) |
| 4 | Brukere: forhåndsdefinert / kan opprettes | [drift.md – Brukere](drift.md#3-brukere), `Data/IdentitySeeder.cs`, `Data/DemoDataSeeder.cs` |
| 5 | Arkitektur, funksjoner, navigasjon, sikkerhet | [arkitektur.md](arkitektur.md), [brukerveiledning.md](brukerveiledning.md), [sikkerhet.md](sikkerhet.md), [sami-og-matching.md](sami-og-matching.md) |
| 6 | Dokumentasjon av tester og resultater | [testing.md](testing.md) |
| 7 | App og MariaDB i Docker, koblet sammen | `docker-compose.yml` (tjenestene `app` og `mariadb`), `ApplicationDbContext.cs` |
| 8 | Registrering av ny bruker, lagres i databasen | `AccountController.Register`, `Views/Home/Index.cshtml`, tabellen `AspNetUsers` |
| 9 | Innlogging + legge til / endre / slette ressurs | `AccountController.Login`, `TwoFactorController`, `ResourceController` (Register/Edit/Delete) |
| 10 | Funksjonalitet fra oppdragsgiver | Behov, matching, forespørsler, varsler, rapport, admin, Sami ([brukerveiledning.md](brukerveiledning.md)) |
| 11 | Kart i skjema der man setter punkt | `Views/Resource/RegisterResource.cshtml`, `Views/Need/RegisterNeed.cshtml`, `wwwroot/js/kartverket.js`, `resource-address.js` |
| 12 | Mobiltilpasset frontend | `@media`-regler i `wwwroot/css/` og views, hamburgermeny i `_PortalHeader.cshtml` |
| 13 | Enkel oversikt over ressurser | `Views/Resource/ResourceOverview.cshtml` (liste, filter, kart), `Views/Home/Dashboard.cshtml` |
| 14 | Sikkerhetstiltak | [sikkerhet.md](sikkerhet.md) |
| 15 | Enhets-, system-, sikkerhets- og brukervennlighetstesting | `FirstWebAppInDocker.Tests/`, [testing.md](testing.md) |
| 16 | Dokumentasjon i GitHub: drift, arkitektur, testscenarier og resultater | [drift.md](drift.md), [arkitektur.md](arkitektur.md), [testing.md](testing.md) |
| 17 | Dokumentasjon i koden | Alle `.cs`, `.cshtml`, `.js` og `.css`-filer, felles stil i [kodestil.md](kodestil.md) |
| 18 | Controller, ViewModel og View | `controllers/`, `ViewModels/`, `Views/` |
| 19 | Responsive sider med dynamisk innhold fra serveren | Sami (fetch/JSON), varselklokka, «Sjekk e-posten din» (spør serveren), Sami-oppsummering i rapporten |
| 20 | GET og POST | Alle controllere: GET viser sider, POST tar imot skjemaer (`[HttpGet]`/`[HttpPost]`) |
| 21 | Skjema → data vises på en annen side | «Registrer ressurs» → «Mine ressurser»/«Oversikt». «Registrer behov» → «Behov»/«Koordinering». |
| 22 | Kart i skjema → data fra kartet vises på en annen side | Punkt fra kartet i «Registrer ressurs» → markør i «Ressurser», på dashbordet og på «Finn ressurser» (avstand) |
| 23 | Dokumentasjon i GitHub (som 16) | Se 16 |
| 24 | Dokumentasjon i koden (som 17) | Se 17 |
