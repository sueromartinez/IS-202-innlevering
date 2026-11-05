using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// En kobling mellom et behov og en ressurs.
///
/// Flyten er:
/// 1. En offentlig aktør (eventuelt etter forslag fra Sami)
///    sender en forespørsel → Status = Requested.
/// 2. Eieren av ressursen får varsel og bekrefter eller avslår.
/// 3. Ved bekreftelse blir behovet "Tildelt" og ressursen "Opptatt".
///
/// Sami kan aldri opprette en tildeling selv. Den kan bare
/// foreslå, og et menneske må trykke "Send forespørsel".
/// </summary>
public class NeedAssignment
{
    public int Id { get; set; }


    public int NeedId { get; set; }

    public Need? Need { get; set; }


    public int ResourceId { get; set; }

    public Resource? Resource { get; set; }


    public AssignmentStatus Status { get; set; }
        = AssignmentStatus.Requested;


    /// <summary>
    /// Hvorfor ressursen passer, vises for den frivillige.
    /// </summary>
    [StringLength(500)]
    public string? Message { get; set; }


    /// <summary>
    /// Identity-ID til den offentlige aktøren som sendte forespørselen.
    /// </summary>
    [StringLength(255)]
    public string RequestedByUserId { get; set; }
        = string.Empty;


    /// <summary>
    /// True hvis forespørselen kom fra et forslag fra Sami.
    /// Brukes i loggen, slik at man kan se hva AI-en har bidratt med.
    /// </summary>
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
