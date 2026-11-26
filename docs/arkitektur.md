# Arkitektur

## Oversikt

Beredskapsportalen er en ASP.NET Core MVC-applikasjon (.NET 10) med MariaDB som database. Begge kjører i Docker og startes med Docker Compose.

```
┌───────────────────────── Nettleser ─────────────────────────┐
│ Razor-sider (HTML/CSS) · Leaflet-kart · sami.js (fetch/JSON) │
└──────────────┬───────────────────────────────┬──────────────┘
               │ GET / POST (skjema + CSRF)    │ JSON
┌──────────────▼───────────────────────────────▼──────────────┐
│ ASP.NET Core (container "app")                              │
│  Middleware: statiske filer → routing → session →           │
│              autentisering → autorisering                   │
│  Controllers  – tilgangskontroll, validering, velger svar   │
│  Services     – regler: matching, forespørsler, varsler,    │
│                 aktivitetslogg, rapporter, Sami             │
│  EF Core      – ApplicationDbContext (+ Identity-tabeller)  │
└───────┬───────────────────┬───────────────────┬─────────────┘
        │                   │                   │
┌───────▼──────┐  ┌─────────▼────────┐  ┌──────▼──────────────┐
│ MariaDB 11.4 │  │ Mailpit (e-post) │  │ Ollama / Claude API  │
│ "mariadb"    │  │ "mailpit"        │  │ (Sami)               │
└──────────────┘  └──────────────────┘  └──────────────────────┘
                 Eksterne tjenester: Kartverket (kart og adresser), OpenStreetMap
```

## Lag og mapper

| Mappe | Ansvar |
|---|---|
| `controllers/` | Én controller per område (Resource, Need, Match, Request, Account, TwoFactor, Admin, Report, Notification, Sami, Home). Håndterer GET og POST, sjekker tilgang og validerer. |
| `ViewModels/` | Det et skjema eller en side trenger. Skiller skjema fra database og hindrer overposting. |
| `Views/` | Razor-sider (.cshtml) som lages på serveren. `Shared/` har layout, meny og Sami-chatten. |
| `Models/` | Entitetene som blir tabeller: `Resource`, `Need`, `NeedAssignment`, `Notification`, `AuditLogEntry`, `ApplicationUser`. |
| `Data/` | `ApplicationDbContext` (EF Core) og seedere for roller, brukere og eksempeldata. |
| `Services/` | Forretningslogikk. `Matching/` (søk og forespørsler), `Notifications/`, `Reports/`, `Ai/` (Sami), `AuditLog.cs`. |
| `Migrations/` | EF Core-migrasjoner. Kjøres automatisk ved oppstart. |
| `wwwroot/` | CSS, JavaScript (`kartverket.js`, `sami.js`, `resource-address.js`, `theme.js`) og bilder. |
| `FirstWebAppInDocker.Tests/` | xUnit-tester. |

## Datamodell

```
ApplicationUser 1───* Resource           (OwnerUserId)
ApplicationUser 1───* Need               (OwnerUserId)
Need            1───* NeedAssignment *───1 Resource
ApplicationUser 1───* Notification
AuditLogEntry   (frittstående logg: bruker-ID, e-post, handling, tidspunkt, IP)
```

`NeedAssignment` er koblingen mellom behov og ressurs (mange-til-mange) med egen status: Requested → Confirmed / Declined / Cancelled.

## Viktige designvalg

| Valg | Begrunnelse |
|---|---|
| MVC med server-rendrede sider | Enkelt, ingen egen frontend-app, innebygd validering og CSRF. |
| EF Core + migrasjoner | Databasen er versjonskontrollert i Git og bygges automatisk. Parameteriserte spørringer. |
| ASP.NET Core Identity | Ferdig og testet passordhashing, roller, lockout og tokens. |
| Services med interface og DI | Samme regler for nettsiden, Sami og testene. E-post og SMS kan byttes uten å endre resten. |
| Repository for matching | Matchingen er ikke låst til EF Core. |
| Grovfilter i SQL + Haversine i C# | Raskt nok for vårt datavolum, uten romlige indekser. |
| Optimistisk samtidighet + atomisk UPDATE | To personer kan ikke bekrefte samme ressurs samtidig. |
| Leaflet + Kartverket | Gratis, lett, og norske topografiske kart, sjøkart og flybilder. |
| Sami med verktøy (tool calling) | Svar bygger på ekte data, verktøyene er begrenset av rollen, og mennesket bekrefter alt. |

## Flyt: fra skjema til visning (eksempel)

1. Frivillig åpner **Registrer ressurs** (GET `/Resource/RegisterResource`).
2. Setter et punkt i kartet. Koordinatene legges i skjulte felt.
3. Trykker **Registrer ressurs** (POST). Controlleren sjekker CSRF og validerer ViewModel-en, setter eier fra innlogget bruker og lagrer i MariaDB.
4. Brukeren sendes til **Oversikt** (Post-Redirect-Get).
5. Ressursen vises nå på andre sider: **Mine ressurser** (liste og kart), dashbordet, og på **Finn ressurser** for offentlige aktører, sortert etter avstand fra behovet.

Mer om matching og Sami: [sami-og-matching.md](sami-og-matching.md).
