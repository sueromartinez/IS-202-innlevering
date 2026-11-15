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
    public DbSet<Resource> Resources => Set<Resource>();


    /// <summary>
    /// Behov registrert av offentlige aktører.
    /// </summary>
    public DbSet<Need> Needs => Set<Need>();


    /// <summary>
    /// Forespørsler/tildelinger mellom behov og ressurser.
    /// </summary>
    public DbSet<NeedAssignment> NeedAssignments => Set<NeedAssignment>();


    /// <summary>
    /// Varsler som vises under varselklokken.
    /// </summary>
    public DbSet<Notification> Notifications => Set<Notification>();


    /// <summary>
    /// Audit-logg: hvem gjorde hva, og når.
    /// </summary>
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();


    protected override void OnModelCreating(
        ModelBuilder builder
    )
    {
        // Identity-tabellene må konfigureres først.
        base.OnModelCreating(builder);


        builder.Entity<NeedAssignment>(entity =>
        {
            // Slettes et behov eller en ressurs,
            // forsvinner også forespørslene som gjelder dem.
            entity.HasOne(assignment => assignment.Need)
                .WithMany()
                .HasForeignKey(assignment => assignment.NeedId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(assignment => assignment.Resource)
                .WithMany()
                .HasForeignKey(assignment => assignment.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(assignment => assignment.Status);
        });


        builder.Entity<Notification>(entity =>
        {
            // Varselklokken spør ofte etter uleste varsler for én bruker.
            entity.HasIndex(notification =>
                new { notification.UserId, notification.IsRead });
        });


        builder.Entity<AuditLogEntry>(entity =>
        {
            // Loggen vises nyeste først, og kan filtreres på handling.
            entity.HasIndex(entry => entry.Timestamp);
            entity.HasIndex(entry => entry.Action);
        });
    }
}
