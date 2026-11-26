# Sikkerhetstiltak

| Område | Tiltak | Hvor i koden |
|---|---|---|
| **Passord** | Hashes av ASP.NET Core Identity og lagres aldri som tekst. Krav: minst 8 tegn med stor og liten bokstav, tall og spesialtegn. | `Program.cs` (AddIdentity) |
| **Brute force** | Kontoen låses i 10 minutter etter 5 feil passord. | `Program.cs` (Lockout) |
| **Tofaktor** | 6-sifret engangskode på e-post. Koden lages med en sikker tilfeldighetsgenerator, lagres kryptert på serveren i 5 minutter og slettes etter bruk. «Send ny kode» er begrenset. | `TwoFactorController.cs`, `Services/UniqueEmailTokenProvider.cs` |
| **E-postbekreftelse** | Kontoen virker ikke før e-posten er bekreftet. Bytte av e-post må bekreftes fra den nye adressen. | `EmailVerificationController.cs`, `AccountController.cs` |
| **Brukeroppramsing** | Samme feilmelding ved ukjent e-post, feil passord og «Glemt passord», så ingen kan finne ut hvilke e-poster som finnes. | `AccountController.cs` |
| **Autorisering (roller)** | `[Authorize(Roles = ...)]` på controllere og metoder. Menyen skjuler i tillegg det rollen ikke har tilgang til. | Alle controllere |
| **Eierskap** | Frivillige ser og endrer bare egne ressurser, og offentlige aktører endrer bare egne behov. Filteret ligger i databasespørringen. Andres data gir 404. | `ResourceController.cs`, `NeedController.cs`, `MatchController.cs` |
| **Registrering av roller** | Nye brukere blir alltid frivillige. Offentlige aktører opprettes bare av admin. Admin kan ikke endre egen rolle. | `AccountController.cs`, `AdminController.cs` |
| **CSRF** | `[ValidateAntiForgeryToken]` på alle POST-handlinger (også JSON-kall fra Sami). Utlogging er POST. | Alle controllere, `sami.js` |
| **Overposting** | Skjemaer bindes til ViewModels. Eier og status settes på serveren. | `ViewModels/` |
| **Validering** | Attributter og `Validate()` på serveren, i tillegg til i nettleseren. | `ViewModels/` |
| **SQL-injection** | EF Core sender alle verdier som parametere. | `Data/`, `Services/` |
| **XSS** | Razor gjør all utskrift med `@` ufarlig. `Html.Raw` brukes bare på egne SVG-ikoner og JSON fra System.Text.Json. `sami.js` bruker `escapeHtml`/`textContent`. | `Views/`, `wwwroot/js/sami.js` |
| **Cookies** | HttpOnly, SameSite=Lax, utløper etter 60 minutter. Krever HTTPS utenfor utviklingsmiljøet. | `Program.cs` |
| **Sikkerhetsheadere** | `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy`, og ingen `Server`-header. HSTS i produksjon. | `Program.cs` |
| **Hemmeligheter** | Passord og API-nøkkel ligger i `.env`, som ikke er i Git. Compose stopper hvis de mangler. | `docker-compose.yml`, `.env.example` |
| **Docker** | Databasen og appen er bare tilgjengelige fra egen maskin (127.0.0.1). Appen kjører som en vanlig bruker, ikke root. | `docker-compose.yml`, `Dockerfile` |
| **Samtidighet** | Optimistisk samtidighetskontroll og atomisk reservasjon hindrer dobbeltbooking. | `ApplicationDbContext.cs`, `AssignmentService.cs` |
| **Sporbarhet** | Aktivitetslogg over innlogginger, endringer, forespørsler, rolleendringer og eksport. | `Services/AuditLog.cs` |
| **Eksport** | CSV er beskyttet mot formelinjeksjon. | `Services/Reports/ReportService.cs` |
| **AI (Sami)** | Verktøyene er begrenset av rollen og sjekkes to ganger. Sami lagrer og sender aldri noe selv. Ingen kontaktinfo sendes til modellen. Grense på antall meldinger og lengde. | `Services/Ai/SamiTools.cs`, `SamiController.cs` |
| **Personvern** | Kontaktinfo vises først etter bekreftelse. Brukeren kan slette kontoen sin og alle egne data. | `RequestController.cs`, `AccountController.cs` |

## Kjente begrensninger

- Content-Security-Policy er ikke slått på ennå. Kart og Leaflet fra eksterne domener må kartlegges først.
- Lokalt kjører appen over HTTP i Docker (Development). I produksjon må den kjøre bak HTTPS.
- SMS er simulert.
