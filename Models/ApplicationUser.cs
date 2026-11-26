/*
    ApplicationUser.cs – en bruker

    Hva filen gjør: Arver IdentityUser, som allerede har e-post, passord-hash og telefon, og
    legger til navn og når kontoen ble opprettet. Rollen lagres i Identity sine rolletabeller,
    ikke her.

    På nettsiden: Navnet vises øverst i menyen og på profilsiden. Lagres i tabellen AspNetUsers
    når noen registrerer seg.

    Oppgavekrav: 8
*/

using Microsoft.AspNetCore.Identity;

namespace FirstWebAppInDocker.Models;

/*
    Representerer en bruker i Beredskapsportalen.

    ASP.NET Core Identity håndterer blant annet:
    - unik bruker-ID
    - e-post
    - brukernavn
    - passordhash
    - tofaktorautentisering
    - lockout
    - sikkerhetstokens
*/
public class ApplicationUser : IdentityUser
{
    // Navnet som vises i Beredskapsportalen.
    public string Name { get; set; } = string.Empty;

    /*
        Tidspunkt brukeren ble opprettet.
        Lagres i UTC.
    */
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
