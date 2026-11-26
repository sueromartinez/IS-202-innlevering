/*
    ApplicationDbContext.cs – koblingen mellom koden og databasen

    Hva filen gjør: Forteller EF Core hvilke tabeller databasen har. Vi jobber med vanlige
    C#-objekter (Resource, Need ...), og EF Core lager SQL-en for oss. Arver fra
    IdentityDbContext, så databasen også får Identity sine tabeller for brukere og roller.
    Hvorfor EF Core: vi får migrasjoner (endringer i databasen lagres i Git sammen med koden),
    og alle verdier sendes som parametere, som beskytter mot SQL-injection.

    På nettsiden: Alt som vises i lister, kart og oversikter, er hentet gjennom denne klassen.

    Oppgavekrav: 1, 7
*/

using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Data;

/*
    Representerer databasekonteksten for Beredskapsportalen.
    Håndterer ASP.NET Core Identity og applikasjonens egne entiteter
    gjennom Entity Framework Core.

    Konteksten benyttes av Identity, tjenester og datatilgangslaget.
    Databaseleverandøren konfigureres gjennom Dependency Injection
    i Program.cs.
*/
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    #region Felt og konstruktør

    /*
        Hver DbSet er én tabell i databasen.
        Resources: ressursene frivillige har registrert. Needs: behovene offentlige aktører har
        registrert. NeedAssignments: forespørslene som kobler et behov til en ressurs.
        Notifications: varslene i varselklokka. AuditLog: aktivitetsloggen admin ser.
    */
    public DbSet<Resource> Resources => Set<Resource>();

    // Behov registrert av offentlige aktører.
    public DbSet<Need> Needs => Set<Need>();

    // Forespørsler og tildelinger mellom behov og ressurser.
    public DbSet<NeedAssignment> NeedAssignments => Set<NeedAssignment>();

    // Varsler knyttet til brukere i Beredskapsportalen.
    public DbSet<Notification> Notifications => Set<Notification>();

    // Revisjonslogg over handlinger utført i systemet.
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    /*
        Oppretter en databasekontekst med konfigurerte EF Core-innstillinger.

        options:
        Innstillinger for databasetilkobling og Entity Framework Core.
    */
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    #endregion

    #region Modellkonfigurasjon

    /*
        Her bestemmer vi relasjoner, hva som skjer ved sletting, indekser og beskyttelse mot at
        to personer endrer samme rad samtidig.
    */
    protected override void OnModelCreating(ModelBuilder builder)
    {
        /*
            Identity-konfigurasjonen må registreres før
            applikasjonens egne entiteter konfigureres.
        */
        base.OnModelCreating(builder);

        ConfigureNeedAssignments(builder);
        ConfigureNotifications(builder);
        ConfigureAuditLog(builder);
    }

    /*
        Konfigurerer relasjoner, indekser og samtidighetskontroll
        for forespørsler og tildelinger.

        builder:
        Modellbyggeren som konfigurerer EF Core-entitetene.
    */
    private static void ConfigureNeedAssignments(ModelBuilder builder)
    {
        builder.Entity<NeedAssignment>(entity =>
        {
            /*
                Eksisterende kaskadesletting beholdes.
                Sletting av behov eller ressurser fjerner
                tilhørende forespørsler og tildelinger.
            */
            entity
                .HasOne(assignment => assignment.Need)
                .WithMany()
                .HasForeignKey(assignment => assignment.NeedId)
                // Slettes et behov, slettes forespørslene som hører til det automatisk.
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasOne(assignment => assignment.Resource)
                .WithMany()
                .HasForeignKey(assignment => assignment.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            /*
                Indekser gjør søk raskere, for eksempel "finn alle forespørsler som venter på
                svar".
            */
            entity.HasIndex(assignment => assignment.Status);

            /*
                NYTT:
                Beskytter mot at en operasjon overskriver
                en tildelingsstatus som er endret av
                en annen databaseoperasjon.

                EF Core inkluderer den opprinnelige statusen
                som en ekstra betingelse ved UPDATE.

                Hvis statusen er endret i mellomtiden,
                oppdateres ingen rader, og EF Core kaster
                DbUpdateConcurrencyException.

                Vi bruker den eksisterende Status-kolonnen.
                Ingen ny databasekolonne er nødvendig.
            */
            entity
                .Property(assignment => assignment.Status)
                /*
                    Beskyttelse mot samtidige endringer. Eksempel: den frivillige trykker
                    «Bekreft» akkurat idet den offentlige aktøren trykker «Avbryt». Har statusen
                    endret seg siden siden ble lastet, stopper EF Core lagringen, og brukeren
                    får beskjed i stedet for at den ene endringen overskriver den andre.
                */
                .IsConcurrencyToken();
        });
    }

    /*
        Konfigurerer indekser for brukerens varsler.

        builder:
        Modellbyggeren som konfigurerer EF Core-entitetene.
    */
    private static void ConfigureNotifications(ModelBuilder builder)
    {
        builder.Entity<Notification>(entity =>
        {
            // Støtter oppslag etter bruker og lest-status.
            entity.HasIndex(notification => new
            {
                /*
                    Varselklokka spør hele tiden "hvor mange uleste varsler har denne
                    brukeren?". Denne indeksen gjør det spørsmålet raskt.
                */
                notification.UserId,
                notification.IsRead
            });
        });
    }

    /*
        Konfigurerer indekser for revisjonsloggen.

        builder:
        Modellbyggeren som konfigurerer EF Core-entitetene.
    */
    private static void ConfigureAuditLog(ModelBuilder builder)
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
