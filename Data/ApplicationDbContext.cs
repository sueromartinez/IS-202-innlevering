using FirstWebAppInDocker.Models;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Data;

/// <summary>
/// Databasekontekst for applikasjonen.
/// Håndterer kommunikasjonen mellom EF Core og MariaDB.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options
    )
        : base(options)
    {
    }


    /// <summary>
    /// Ressurser som lagres i databasen.
    /// </summary>
    public DbSet<Resource> Resources => Set<Resource>();


    /// <summary>
    /// Behov registrert av offentlige aktører.
    /// </summary>
    public DbSet<Need> Needs => Set<Need>();
}