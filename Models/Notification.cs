using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Et varsel som vises under varselklokken i portalen.
/// Det samme varselet sendes også på e-post (og simulert SMS)
/// av NotificationService.
/// </summary>
public class Notification
{
    public int Id { get; set; }


    /// <summary>
    /// Identity-ID til brukeren som skal få varselet.
    /// </summary>
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


    /// <summary>
    /// Relativ lenke i portalen, f.eks. "/Request".
    /// </summary>
    [StringLength(300)]
    public string? Link { get; set; }


    public bool IsRead { get; set; }


    public DateTime CreatedAt { get; set; }
        = DateTime.UtcNow;
}
