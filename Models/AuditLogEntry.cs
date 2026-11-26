/*
    AuditLogEntry.cs – en rad i aktivitetsloggen

    Hva filen gjør: Hvem gjorde hva, når og fra hvilken IP-adresse. Lagrer også e-posten, slik
    at loggen er lesbar selv om brukeren slettes senere.

    På nettsiden: Siden «Aktivitetslogg» som bare admin har tilgang til.

    Oppgavekrav: 14
*/

using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/*
    Én linje i audit-loggen: hvem gjorde hva, og når.

    Loggen gjør det mulig å etterprøve viktige handlinger i en krise,
    f.eks. hvem som sendte en forespørsel, endret en rolle eller slettet en ressurs.
    Linjene endres eller slettes aldri fra portalen.
*/
public class AuditLogEntry
{
    public long Id { get; set; }

    public DateTime Timestamp { get; set; }
        = DateTime.UtcNow;

    // Identity-ID til brukeren som utførte handlingen (null hvis ukjent).
    [StringLength(255)]
    public string? UserId { get; set; }

    /*
        E-posten på handlingstidspunktet, så loggen kan leses selv om
        brukeren senere endrer e-post.
    */
    [StringLength(150)]
    public string? UserEmail { get; set; }

    // Kort kode for handlingen, f.eks. "ResourceDeleted". Se AuditActions.
    [Required]
    [StringLength(60)]
    public string Action { get; set; }
        = string.Empty;

    // Lesbar beskrivelse, f.eks. «Slettet ressursen «Sjark» (#12)».
    [Required]
    [StringLength(500)]
    public string Details { get; set; }
        = string.Empty;

    [StringLength(45)]
    public string? IpAddress { get; set; }
}
