using Microsoft.AspNetCore.Identity;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Representerer en bruker i Beredskapsportalen.
///
/// ASP.NET Core Identity håndterer blant annet:
/// - unik bruker-ID
/// - e-post
/// - brukernavn
/// - passordhash
/// - tofaktorautentisering
/// - lockout
/// - sikkerhetstokens
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// Navnet som vises i Beredskapsportalen.
    /// </summary>
    public string Name { get; set; } = string.Empty;


    /// <summary>
    /// Tidspunkt brukeren ble opprettet.
    /// Lagres i UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;
}