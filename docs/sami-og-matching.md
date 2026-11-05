# Sami og matching – arkitektur

Dette dokumentet beskriver hvordan AI-assistenten Sami og matchingen mellom behov og ressurser er bygget.
Diagrammet kan limes inn i https://mermaid.live for å lage et bilde til rapporten.

## Komponentdiagram

```mermaid
flowchart LR
    subgraph Nettleser
        W[Sami chat-widget<br/>_Sami.cshtml + sami.js]
        P[Portalsider<br/>Behov, Ressurser, Forespørsler]
    end

    subgraph "ASP.NET Core (app)"
        SC[SamiController<br/>POST /Sami/Chat]
        MC[MatchController<br/>Finn ressurser / Send forespørsel]
        RC[RequestController<br/>Forespørsler for frivillige]
        SA[SamiAssistantService<br/>tool-loop]
        ST[SamiTools<br/>verktøy per rolle]
        MS[ResourceMatchingService<br/>Haversine + filtre]
        AS[AssignmentService<br/>statusflyt]
        NS[NotificationService<br/>varsel, e-post, SMS]
        CM[IChatModelClient<br/>Ollama / Claude]
        GEO[NominatimGeocoder]
    end

    DB[(MariaDB)]
    OL[Ollama<br/>lokal språkmodell]
    CL[Claude API<br/>valgfri]
    MP[Mailpit<br/>test-e-post]
    NOM[Nominatim / OSM]

    W --> SC --> SA
    SA --> CM
    CM --> OL
    CM -.-> CL
    SA --> ST
    ST --> MS
    ST --> GEO --> NOM
    W -- "Send forespørsel" --> MC
    P --> MC
    P --> RC
    MC --> MS
    MC --> AS
    RC --> AS
    AS --> NS
    NS --> MP
    MS --> DB
    AS --> DB
    NS --> DB
    ST --> DB
```

## Statusflyt for et behov

```mermaid
stateDiagram-v2
    [*] --> Ny
    Ny --> Vurderes: Offentlig aktør sender forespørsel
    Vurderes --> Tildelt: Frivillig bekrefter
    Vurderes --> Ny: Alle forespørsler avslått/avbrutt
    Tildelt --> Løst: Offentlig aktør markerer som løst
    Vurderes --> Løst
    Ny --> Løst
    Løst --> [*]
```

## Viktige designvalg

- **Deterministisk matching.** Avstand (Haversine) og filtrering på type og status gjøres i C#.
  Språkmodellen får ferdige kandidater og forklarer og rangerer dem. Den kan ikke finne på ressurser.
- **Menneske i løkken.** Sami kan bare *foreslå* en match (`suggest_match`). Forespørselen sendes først når
  den offentlige aktøren trykker «Send forespørsel», og tildelingen gjelder først når den frivillige bekrefter.
- **Personvern.** Verktøyene sender aldri e-post eller telefonnummer til modellen. Frivillige ser bare
  omtrentlig avstand til behov, ikke eksakt sted eller hvem som har meldt behovet.
- **Rollebaserte verktøy.** Hver rolle får sitt eget sett med verktøy, og hvert verktøy sjekker rollen
  på nytt (forsvar i dybden). Rollen hentes fra Identity på serveren, aldri fra nettleseren.
- **Utskiftbar modell.** `IChatModelClient` har én implementasjon for Ollama (lokal, gratis) og én for
  Claude API. Valget gjøres i konfigurasjonen.
- **Sporbarhet.** Alle verktøykall logges, og forespørsler fra Sami-forslag merkes (`SuggestedByAssistant`).
