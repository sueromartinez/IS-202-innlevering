/*
    NeedAssignment.cs – en forespørsel

    Hva filen gjør: Kobler et behov til en ressurs. Én rad betyr "dette behovet har spurt om
    denne ressursen". Den har egne opplysninger (status, melding, tidspunkt), derfor er den en
    egen tabell.
    Gangen: offentlig aktør sender forespørsel (Requested) → frivillig bekrefter (Confirmed)
    eller avslår (Declined) → eller forespørselen avbrytes (Cancelled).

    På nettsiden: Den frivillige ser forespørselen under «Forespørsler». Den offentlige aktøren
    ser den under «Tildelinger» og på «Finn ressurser».

    Oppgavekrav: 10
*/

using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/*
    En kobling mellom et behov og en ressurs.

    Flyten er:
    1. En offentlig aktør (eventuelt etter forslag fra Sami)
       sender en forespørsel → Status = Requested.
    2. Eieren av ressursen får varsel og bekrefter eller avslår.
    3. Ved bekreftelse blir behovet "Tildelt" og ressursen "Opptatt".

    Sami kan aldri opprette en tildeling selv. Den kan bare
    foreslå, og et menneske må trykke "Send forespørsel".
*/
public class NeedAssignment
{
    public int Id { get; set; }

    public int NeedId { get; set; }

    public Need? Need { get; set; }

    public int ResourceId { get; set; }

    public Resource? Resource { get; set; }

    public AssignmentStatus Status { get; set; }
        = AssignmentStatus.Requested;

    // Hvorfor ressursen passer, vises for den frivillige.
    [StringLength(500)]
    public string? Message { get; set; }

    // Identity-ID til den offentlige aktøren som sendte forespørselen.
    [StringLength(255)]
    public string RequestedByUserId { get; set; }
        = string.Empty;

    /*
        Viser om forespørselen kom fra et forslag fra Sami.
    */
    public bool SuggestedByAssistant { get; set; }

    public DateTime CreatedAt { get; set; }
        = DateTime.UtcNow;

    public DateTime? RespondedAt { get; set; }
}

public enum AssignmentStatus
{
    [Display(Name = "Venter på svar")]
    Requested,

    [Display(Name = "Bekreftet")]
    Confirmed,

    [Display(Name = "Avslått")]
    Declined,

    [Display(Name = "Avbrutt")]
    Cancelled
}
