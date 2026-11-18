
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Data;

/// <summary>
/// Representerer databasekonteksten for Beredskapsportalen.
/// Håndterer ASP.NET Core Identity og applikasjonens egne entiteter
/// gjennom Entity Framework Core.
/// </summary>
/// <remarks>
/// Konteksten benyttes av Identity, tjenester og datatilgangslaget.
/// Databaseleverandøren konfigureres gjennom Dependency Injection
/// i Program.cs.
/// </remarks>
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    #region Konstruktør

    /// <summary>
    /// Oppretter en databasekontekst med konfigurerte EF Core-innstillinger.
    /// </summary>
    /// <param name="options">
    /// Innstillinger for databasetilkobling og Entity Framework Core.
    /// </param>
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    #endregion

    #region Entiteter

    /// <summary>
    /// Ressurser registrert av ressursleverandører.
    /// </summary>
    public DbSet<Resource> Resources => Set<Resource>();

    /// <summary>
    /// Behov registrert av offentlige aktører.
    /// </summary>
    public DbSet<Need> Needs => Set<Need>();

    /// <summary>
    /// Forespørsler og tildelinger mellom behov og ressurser.
    /// </summary>
    public DbSet<NeedAssignment> NeedAssignments =>
        Set<NeedAssignment>();

    /// <summary>
    /// Varsler knyttet til brukere i Beredskapsportalen.
    /// </summary>
    public DbSet<Notification> Notifications =>
        Set<Notification>();

    /// <summary>
    /// Revisjonslogg over handlinger utført i systemet.
    /// </summary>
    public DbSet<AuditLogEntry> AuditLog =>
        Set<AuditLogEntry>();

    #endregion

    #region Modellkonfigurasjon

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity-konfigurasjonen må registreres før
        // applikasjonens egne entiteter konfigureres.
        base.OnModelCreating(builder);

        ConfigureNeedAssignments(builder);
        ConfigureNotifications(builder);
        ConfigureAuditLog(builder);
    }

    #endregion

    #region Relasjoner og indekser

    /// <summary>
    /// Konfigurerer relasjoner, indekser og samtidighetskontroll
    /// for forespørsler og tildelinger.
    /// </summary>
    /// <param name="builder">
    /// Modellbyggeren som konfigurerer EF Core-entitetene.
    /// </param>
    private static void ConfigureNeedAssignments(
        ModelBuilder builder)
    {
        builder.Entity<NeedAssignment>(entity =>
        {
            // Eksisterende kaskadesletting beholdes.
            // Sletting av behov eller ressurser fjerner
            // tilhørende forespørsler og tildelinger.
            entity
                .HasOne(assignment => assignment.Need)
                .WithMany()
                .HasForeignKey(assignment => assignment.NeedId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(assignment => assignment.Resource)
                .WithMany()
                .HasForeignKey(assignment => assignment.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Brukes ved filtrering av tildelingsstatus.
            entity.HasIndex(assignment => assignment.Status);

            // NYTT:
            // Beskytter mot at en operasjon overskriver
            // en tildelingsstatus som er endret av
            // en annen databaseoperasjon.
            //
            // EF Core inkluderer den opprinnelige statusen
            // som en ekstra betingelse ved UPDATE.
            //
            // Hvis statusen er endret i mellomtiden,
            // oppdateres ingen rader, og EF Core kaster
            // DbUpdateConcurrencyException.
            //
            // Vi bruker den eksisterende Status-kolonnen.
            // Ingen ny databasekolonne er nødvendig.
            entity
                .Property(assignment => assignment.Status)
                .IsConcurrencyToken();
        });
    }

    /// <summary>
    /// Konfigurerer indekser for brukerens varsler.
    /// </summary>
    /// <param name="builder">
    /// Modellbyggeren som konfigurerer EF Core-entitetene.
    /// </param>
    private static void ConfigureNotifications(
        ModelBuilder builder)
    {
        builder.Entity<Notification>(entity =>
        {
            // Støtter oppslag etter bruker og lest-status.
            entity.HasIndex(notification => new
            {
                notification.UserId,
                notification.IsRead
            });
        });
    }

    /// <summary>
    /// Konfigurerer indekser for revisjonsloggen.
    /// </summary>
    /// <param name="builder">
    /// Modellbyggeren som konfigurerer EF Core-entitetene.
    /// </param>
    private static void ConfigureAuditLog(
        ModelBuilder builder)
    {
        builder.Entity<AuditLogEntry>(entity =>
        {
            // Støtter sortering og filtrering av logghendelser.
            entity.HasIndex(entry => entry.Timestamp);
            entity.HasIndex(entry => entry.Action);
        });
    }

    #endregion
}
