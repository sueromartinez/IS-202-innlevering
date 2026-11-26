# Testing

Prosjektet er testet på fire måter: enhetstesting, systemtesting, sikkerhetstesting og brukervennlighetstesting. De automatiske testene ligger i `FirstWebAppInDocker.Tests/` (xUnit, Moq, EF Core InMemory).

> **Til gruppa:** Feltene merket *(fyll inn)* må fylles med deres egne resultater før innlevering. Ingen resultater er funnet på her.

## 1. Enhetstesting og integrasjonstesting (automatisk)

185 enhetstester og 2 integrasjonstester mot ekte MariaDB.

| Testfil | Antall | Hva som testes |
|---|---|---|
| `AccountControllerTests.cs` | 20 | Registrering (riktig rolle, duplikat e-post, feil som ruller tilbake), innlogging (feil passord, ubekreftet e-post, lockout, tofaktor), utlogging, profil, sikkerhetsattributter |
| `TwoFactorControllerTests.cs` | 19 | Sending av kode, feil kode, lockout, «send ny kode» med ventetid, sikkerhetsattributter |
| `MatchingTests.cs` | 23 | Haversine-avstand, nærmeste først, type/status/radius, grensetilfeller, behov for frivillige, forespørsel/bekreft/avslå/løs |
| `NeedControllerTests.cs` | 18 | Rollekrav, CSRF, flere behov i ett skjema, validering, endre/slette eget vs. andres behov |
| `NeedOwnershipTests.cs` | 8 | Eierskap via bruker-ID, også etter e-postbytte og for eldre behov |
| `NeedValidationTests.cs` | 11 | Regler for beskrivelse, antall og standard prioritet |
| `MatchControllerTests.cs` | 8 | Bare eier/admin kan sende, avbryte og løse. Ressursen frigjøres ved avbrudd. |
| `HomeDashboardTests.cs` | 13 | Riktige data per rolle, krav om innlogging, statistikk |
| `HomeControllerTests.cs` | 7 | Validering av «Registrer ressurs» (navn, antall, koordinater, periode, adresse) |
| `ResourceAuthorizationTests.cs` | 11 | Rollekrav, CSRF, riktig eier ved registrering, tilgjengelighetsperioder |
| `ResourceOwnershipTests.cs` | 3 | Frivillige ser bare egne ressurser |
| `ReportAndCoverageTests.cs` | 20 | Rapport, CSV (formelinjeksjon) og JSON, dekning, aktivitetslogg, sletting med historikk |
| `ProfileViewModelTests.cs` | 7 | Regler for navn, e-post og telefonnummer |
| `SamiToolsTests.cs` | 14 | Rollebaserte verktøy, ingen kontaktinfo til AI, forslag og utkast lagrer ingenting |
| `DemoLoginSecurityTests.cs` | 3 | Demo-innlogging er sperret utenfor Development |
| `ResourceConcurrencyIntegrationTests.cs` | 1 | To samtidige bekreftelser av samme ressurs: bare én lykkes (MariaDB) |
| `CancellationConcurrencyIntegrationTests.cs` | 1 | Avbryt og bekreft samtidig gir ikke feil booking (MariaDB) |

Kjøring: se [drift.md](drift.md#5-kjøre-testene).

**Resultat** *(fyll inn etter `dotnet test`)*:

| Dato | Kommando | Bestått | Feilet | Hoppet over |
|---|---|---|---|---|
| | `dotnet test` | | | |
| | `dotnet test --filter Category=Integration` (med MariaDB) | | | |

## 2. Systemtesting (manuell, hele løsningen i Docker)

Utføres mot `docker compose up --build` på http://localhost:8080.

| # | Scenario | Forventet resultat | Resultat *(fyll inn)* |
|---|---|---|---|
| S1 | Start med tom database | Tabeller, roller, admin og eksempeldata lages automatisk | |
| S2 | Registrer ny bruker | Bekreftelses-e-post i Mailpit. Bruker lagret i `AspNetUsers` med rollen frivillig. | |
| S3 | Logg inn med tofaktor | Kode i Mailpit, innlogging etter riktig kode | |
| S4 | Frivillig registrerer ressurs med punkt i kartet | Ressursen vises i «Mine ressurser» og som markør på kartet | |
| S5 | Frivillig endrer og sletter egen ressurs | Endring lagres. Sletting virker ikke for opptatt ressurs. | |
| S6 | Offentlig aktør registrerer to behov i ett skjema | Begge vises under «Behov» | |
| S7 | «Finn ressurser» → «Send forespørsel» | Ressurser sortert etter avstand. Frivillig får varsel i portalen og på e-post. | |
| S8 | Frivillig bekrefter | Ressurs blir Opptatt, behov blir Tildelt, kontaktinfo vises | |
| S9 | «Marker som løst» | Behov Løst, ressurs Tilgjengelig igjen | |
| S10 | Rapport og eksport | Tall stemmer, CSV åpnes i Excel | |
| S11 | Sami: «registrer et aggregat i Kristiansand» | Utkast vises. Lagres først etter klikk. | |
| S12 | Mobilvisning (DevTools / telefon) | Sidene og menyen tilpasser seg smal skjerm | |

## 3. Sikkerhetstesting

| # | Test | Forventet resultat | Hvordan | Resultat *(fyll inn)* |
|---|---|---|---|---|
| T1 | Åpne `/Admin/Users` som frivillig | «Ingen tilgang» | Manuelt + `[Authorize]`-tester | |
| T2 | Endre ID i adressen til en annens ressurs/behov | 404, ingen data vises | Manuelt + eierskapstester | |
| T3 | POST uten CSRF-token | 400 Bad Request | Manuelt (curl/Postman) + attributtester | |
| T4 | 5 feil passord | Kontoen låses i 10 minutter | Manuelt + `Login_LockedOut_*` | |
| T5 | Skrive `<script>alert(1)</script>` i et tekstfelt | Vises som tekst og kjøres ikke | Manuelt | |
| T6 | Celle som starter med `=` i eksport | Vises som tekst i Excel | `Csv_EscapesFieldsAndBlocksFormulas` | |
| T7 | Be Sami om noe rollen ikke har lov til | Avvist | `Volunteer_CallingForbiddenTool_GetsError` | |
| T8 | Demo-innlogging i produksjon | 404 | `DemoLogin_Production_ShouldReturnNotFound` | |
| T9 | Sjekke HTTP-svar for sikkerhetsheadere | `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` finnes | DevTools → Network | |

## 4. Brukervennlighetstesting

**Metode:** think-aloud. Deltakeren løser oppgaver og sier høyt hva hen tenker, mens en observatør noterer. *(Juster hvis dere brukte en annen metode.)*

**Deltakere** *(fyll inn)*:

| Deltaker | Bakgrunn | Rolle i testen | Enhet |
|---|---|---|---|
| D1 | | Frivillig | |
| D2 | | Offentlig aktør | |
| D3 | | | |

**Oppgaver og resultater** *(fyll inn)*:

| Oppgave | D1 | D2 | D3 | Observasjoner |
|---|---|---|---|---|
| Registrer deg og logg inn | | | | |
| Registrer en ressurs med punkt i kartet | | | | |
| Finn og endre ressursen din | | | | |
| (Offentlig aktør) Registrer et behov og send en forespørsel | | | | |
| Svar på en forespørsel | | | | |
| Bruk Sami til å finne hjelp | | | | |

Skriv for eksempel ✔ (klarte det), ~ (klarte det med hjelp) eller ✘ (klarte det ikke), og eventuelt tid.

**Funn og endringer vi gjorde** *(fyll inn)*:

| Funn | Alvorlighet | Endring |
|---|---|---|
| | | |
