/*
    Notification.cs – et varsel

    Hva filen gjør: Et varsel til en bruker, lagret i databasen. Link sender brukeren rett til
    riktig side.

    På nettsiden: Tallet på varselklokka øverst og listen på siden «Varsler».

    Oppgavekrav: 10, 19
*/

using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/*
    Et varsel som vises under varselklokken i portalen.
    Det samme varselet sendes også på e-post (og simulert SMS)
    av NotificationService.
*/
public class Notification
{
    public int Id { get; set; }

    // Identity-ID til brukeren som skal få varselet.
    [Required]
    [StringLength(255)]
    public string UserId { get; set; }
        = string.Empty;

    [Required]
    [StringLength(150)]
    public string Title { get; set; }
        = string.Empty;

    [Required]
    [StringLength(500)]
    public string Message { get; set; }
        = string.Empty;

    // Relativ lenke i portalen, f.eks. "/Request".
    [StringLength(300)]
    public string? Link { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }
        = DateTime.UtcNow;
}
