using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Én linje i audit-loggen: hvem gjorde hva, og når.
///
/// Loggen gjør det mulig å etterprøve viktige handlinger i en krise,
/// f.eks. hvem som sendte en forespørsel, endret en rolle eller slettet en ressurs.
/// Linjene endres eller slettes aldri fra portalen.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }


    public DateTime Timestamp { get; set; }
        = DateTime.UtcNow;


    /// <summary>
    /// Identity-ID til brukeren som utførte handlingen (null hvis ukjent).
    /// </summary>
    [StringLength(255)]
    public string? UserId { get; set; }


    /// <summary>
    /// E-posten på handlingstidspunktet, så loggen kan leses selv om
    /// brukeren senere endrer e-post.
    /// </summary>
    [StringLength(150)]
    public string? UserEmail { get; set; }


    /// <summary>
    /// Kort kode for handlingen, f.eks. "ResourceDeleted". Se AuditActions.
    /// </summary>
    [Required]
    [StringLength(60)]
    public string Action { get; set; }
        = string.Empty;


    /// <summary>
    /// Lesbar beskrivelse, f.eks. «Slettet ressursen «Sjark» (#12)».
    /// </summary>
    [Required]
    [StringLength(500)]
    public string Details { get; set; }
        = string.Empty;


    [StringLength(45)]
    public string? IpAddress { get; set; }
}
