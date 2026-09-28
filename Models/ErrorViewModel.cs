// Samler modellklassene under prosjektets Models-navnerom.
// Semikolonet betyr at navnerommet gjelder for resten av filen.
namespace FirstWebAppInDocker.Models;

// Modellen inneholder informasjon som kan vises på feilsiden.
public class ErrorViewModel
{
    // Lagrer ID-en til forespørselen der feilen oppstod.
    // ID-en kan brukes til å finne igjen forespørselen i logger.
    // string? betyr at verdien kan være null (mangle).
    // get lar oss lese verdien, og set lar oss endre den.
    public string? RequestId { get; set; }

    // Bestemmer om forespørselens ID skal vises på feilsiden.
    // bool betyr at resultatet er enten true eller false.
    // IsNullOrEmpty sjekker om RequestId er null eller tom tekst ("").
    // ! snur resultatet: true hvis ID-en inneholder tekst.
    // => betyr at verdien beregnes fra uttrykket hver gang den leses.
    // Eksempel: RequestId = "abc123" gir true, mens null gir false.
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
