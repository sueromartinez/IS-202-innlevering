using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;

namespace FirstWebAppInDocker.ViewModels;

/// <summary>
/// En rad i brukeroversikten for admin.
/// </summary>
public class AdminUserViewModel
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public bool IsDeactivated { get; init; }

    public bool IsTemporarilyLocked { get; init; }

    public bool IsCurrentUser { get; init; }

    public DateTime CreatedAt { get; init; }
}


/// <summary>
/// Skjema for at admin oppretter en offentlig aktør.
/// Offentlige aktører kan ikke registrere seg selv,
/// fordi de får se alle ressurser med kontaktinformasjon.
/// </summary>
public class CreatePublicActorViewModel
{
    [Required(ErrorMessage = "Navn er påkrevd.")]
    [StringLength(100, ErrorMessage = "Navnet kan maksimalt være 100 tegn.")]
    [Display(Name = "Navn / etat")]
    public string Name { get; set; } = string.Empty;


    [Required(ErrorMessage = "E-post er påkrevd.")]
    [EmailAddress(ErrorMessage = "Skriv inn en gyldig e-postadresse.")]
    [StringLength(150)]
    [Display(Name = "E-post")]
    public string Email { get; set; } = string.Empty;


    [Required(ErrorMessage = "Midlertidig passord er påkrevd.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Passordet må være minst 8 tegn.")]
    [DataType(DataType.Password)]
    [Display(Name = "Midlertidig passord")]
    public string Password { get; set; } = string.Empty;
}


/// <summary>
/// Aktivitetsloggen for administrator.
/// </summary>
public class AuditLogViewModel
{
    public List<AuditLogEntry> Entries { get; init; } = new();

    public string? Action { get; init; }

    public string? Query { get; init; }

    public int Days { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalCount { get; init; }

    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));

    public Dictionary<string, int> ActionCounts { get; init; } = new();
}
