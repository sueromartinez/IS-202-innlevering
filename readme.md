IS-202-innlevering-1

KI REDEGJØRLESE
KI er et verdifullt verktøy som gruppen har valgt å benytte seg av i løpet av prosjektet. I arbeidet med prosjektet har gruppen valgt å bruke KI til å hjelpe med ulik problematikk. Hovedsakelig for å tolke og forstå nye fagbegreper innad koding, ideutvikling, foreslått forbedring i kode dersom koden ikke har vært helt fungerende eller gunstig, samt andre hindre man har opplevd ved at koden ikke kjører. KI verktøy som Microsoft Co-pilot og Claude.ai har gruppen hovedsakelig brukt i dette prosjektet.
KI har vært et viktig element for å styrke og øke gruppens faglige kompetanse på hvordan KI kan være et godt verktøy i prosjektsammenheng og i teknologien, forutsett at man bruker KI på en ansvarsfull måte.


KILDER;
Microsoft. (2026). Copilot [AI assistent]. https://copilot.microsoft.com
Anthropic. (2026). Claude [AI assistent]. https://claude.ai

---

# Kjøre prosjektet lokalt med Docker

## Forutsetninger

Før prosjektet startes må følgende være installert:

- Git
- Docker Desktop

## 1. Klon repository

git clone https://github.com/sueromartinez/IS-202-innlevering.git
cd IS-202-innlevering

## 2. Opprett lokale miljøvariabler

Kopier eksempel-filen:

Copy-Item .env.example .env

Åpne `.env` og velg egne lokale passord.

## 3. Start prosjektet

docker compose up --build

Applikasjonen åpnes på:

http://localhost:8080

Databasen og nødvendige tabeller opprettes automatisk.

## Stoppe prosjektet

docker compose down
