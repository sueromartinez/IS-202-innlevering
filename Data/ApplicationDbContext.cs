using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Data;

/// <summary>
/// Databasekontekst for Beredskapsportalen.
///
/// IdentityDbContext gjør at samme EF Core-kontekst
/// håndterer både:
/// - ASP.NET Core Identity-brukere
/// - roller
/// - innlogging/2FA-data
/// - applikasjonens egne tabeller, som Resources
/// </summary>
public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options
    )
        : base(options)
    {
    }


    /// <summary>
    /// Ressurser som lagres i MariaDB.
    /// </summary>
    public DbSet<Resource> Resources =>
        Set<Resource>();
}