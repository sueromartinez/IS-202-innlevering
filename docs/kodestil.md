# Kodestil

Seks personer har skrevet koden. Disse reglene gjør at den ser lik ut uansett hvem som skrev hva. Følg dem når dere endrer eller legger til kode.

## Oppbygning av en controller eller service

Alle controllere og services har samme rekkefølge og de samme regionene:

```csharp
public class ResourceController : Controller
{
    #region Felt og konstruktør
    // konstanter → statiske felt → felt → egenskaper → konstruktør
    #endregion

    #region Registrer ressurs        // én region per side eller funksjon
    // GET-metoden først, så POST-metoden med samme navn
    #endregion

    #region Hjelpemetoder
    // private metoder
    #endregion
}
```

Tips: i VS Code lukker **Ctrl+K Ctrl+0** alle regioner, så man ser oversikten over en fil.

Små hjelpeklasser (`ResourceDisplay`, `GeoDistance`, `EnumDisplay`, `ResourceAvailability`), modeller og ViewModels har ikke regioner.

## Navn

| Hva | Regel | Eksempel |
|---|---|---|
| Databasekonteksten | Alltid `_context` | `private readonly ApplicationDbContext _context;` |
| Private felt | `_` + camelCase | `_userManager`, `_chatModel`, `_geocoder` |
| Melding etter handling (TempData) | `<Område>Success` eller `<Område>Error` | `TempData["ResourceSuccess"]`, `TempData["MatchError"]` |
| Metoder | PascalCase. Asynkrone metoder i services slutter på `Async`. | `FindNearbyResourcesAsync` |
| Kommentarer og tekst til brukeren | Norsk | |

## Typer

- Skriv typen i stedet for `var`: `List<Resource> resources = ...`.
- Unntak: anonyme typer (`.Select(x => new { ... })`), der typen ikke kan skrives.
- Testprosjektet bruker `var`, som er vanlig i tester.

## Formatering

- 4 mellomrom innrykk, og `{` på egen linje.
- Får et metodekall plass på én linje (maks ca. 100 tegn), skrives det på én linje.
- Er det for langt, kommer ett argument per linje, og `)` står på samme linje som det siste argumentet.
- Én tom linje mellom metoder. Ingen tom linje rett etter `{` eller rett før `}`.
- `using`-linjene øverst er sortert alfabetisk, med `System` først.

## Kommentarer

- Øverst i hver fil står en forklaring med samme oppbygning: *Hva filen gjør*, *På nettsiden* og *Oppgavekrav*.
- Kommentarer over flere linjer skrives som én blokk (`/* ... */`, i views `@* ... *@`), ikke som mange `//`-linjer.
- Kommentarer på én linje skrives som `// tekst`.
- Over en action står adressen og hva den gjør på nettsiden:

```csharp
/*
    POST /Resource/RegisterResource
    På nettsiden: kjøres når den frivillige trykker «Registrer ressurs».
*/
```
