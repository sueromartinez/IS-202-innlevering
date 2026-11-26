# Brukerveiledning og navigasjon

Portalen har tre roller. Menyen øverst viser bare det rollen har tilgang til, og serveren sjekker tilgangen uansett (å skrive en adresse direkte hjelper ikke).

## Felles for alle

- **Forside** (`/`): informasjon, **Logg inn** og **Registrer**.
- **Varselklokka** i menyen: nye forespørsler, svar og tilbud. Klikk for å åpne **Varsler**.
- **Profil**: endre navn, telefon og e-post, se status for e-post og tofaktor, og slette konto.
- **Sami**: den runde knappen nede til høyre. Svarer på spørsmål om portalen og hjelper med oppgavene under.
- **Tema**: lyst/mørkt og fargeblind modus (huskes i nettleseren).
- Alle sidene er tilpasset mobil. På smale skjermer blir menyen en meny man åpner med en knapp.

## Frivillig / ressursleverandør

Meny: **Oversikt · Mine ressurser · Registrer ressurs · Forespørsler · Behov jeg kan hjelpe med · Profil**

1. **Registrer ressurs**: velg type, skriv navn, antall og beskrivelse, søk opp adressen eller klikk i kartet, velg hvor lenge den er tilgjengelig, og trykk **Registrer ressurs**.
2. **Mine ressurser**: liste og kart over egne ressurser. Sett **Tilgjengelig/Utilgjengelig**, **Endre** eller **Slett**. Opptatte ressurser og ressurser med historikk kan ikke slettes.
3. **Forespørsler**: når en offentlig aktør spør om en ressurs, svar **Bekreft** eller **Avslå**. Kontaktinfo til den som spør vises etter bekreftelse.
4. **Behov jeg kan hjelpe med**: åpne behov i nærheten av egne ressurser. **Tilby hjelp** sender et varsel til den offentlige aktøren.
5. **Sami** kan lage et utkast til en ressurs ut fra en beskrivelse i chatten. Den lagres først når du trykker **Registrer ressursen**.

## Offentlig aktør

Meny: **Oversikt · Behov · Registrer behov · Ressurser · Koordinering · Rapporter · Profil**

1. **Registrer behov**: ett eller flere behov i samme skjema, hvert med type, prioritet (akutt/planlagt), antall, punkt i kartet, frist og kontaktperson.
2. **Behov**: alle behov. Egne behov kan endres og slettes.
3. **Ressurser**: alle registrerte ressurser i liste og kart.
4. **Koordinering**: tavle over aktive behov (trenger ressurser · venter på svar · bekreftet).
   - **Finn ressurser** på et behov: passende ressurser sortert etter avstand, med kart. **Send forespørsel** til de du vil ha.
   - **Avbryt** en forespørsel, eller **Marker som løst** når behovet er dekket. Da blir ressursene ledige igjen.
   - **Tildelinger**: alle forespørsler med status og kontaktinfo.
5. **Rapporter**: situasjonsrapport med nøkkeltall, oppsummering fra Sami, ressursliste per kommune for utskrift, og eksport til CSV/JSON.
6. **Sami** kan foreslå ressurser til en hendelse. Forslagene vises som kort med **Send forespørsel**.

## Admin

Alt en offentlig aktør kan, og i tillegg under **Brukere**:
- se alle brukere, **endre rolle**, **deaktivere** og **aktivere**
- **opprette offentlig aktør**
- se og slette ressursene til en bruker
- **Aktivitetslogg**: hvem gjorde hva og når, med filter

## Statusgang

```
Behov:        Nytt → Til vurdering → Tildelt → Løst
Forespørsel:  Sendt → Bekreftet / Avslått / Avbrutt
Ressurs:      Tilgjengelig ⇄ Opptatt (settes av systemet)   ·   Utilgjengelig (settes av eieren)
```
