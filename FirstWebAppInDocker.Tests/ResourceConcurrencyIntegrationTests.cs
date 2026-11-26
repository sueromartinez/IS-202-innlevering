/*
    ResourceConcurrencyIntegrationTests.cs – automatiske tester

    Hva filen gjør: Tester at to samtidige bekreftelser av samme ressurs ikke begge lykkes (mot
    ekte MariaDB). Kjøres mot en egen MariaDB fra docker-compose.integration.yml.

    Oppgavekrav: 6, 15
*/

using System.Data.Common;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Matching;
using FirstWebAppInDocker.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MySqlConnector;
using Xunit;

namespace FirstWebAppInDocker.Tests;

/*
    Tester samtidig bekreftelse av samme ressurs
    mot en separat MariaDB-testdatabase.

    Testen bruker kun:
    127.0.0.1:3308 / beredskap_test_db.

    Krever miljøvariablene:
    BEREDSKAP_RUN_MARIADB_TESTS=1
    BEREDSKAP_TEST_DB_PASSWORD

    Ved vanlig dotnet test hoppes testen over
    dersom integrasjonstester ikke er aktivert.
*/
public class ResourceConcurrencyIntegrationTests
{
    #region Testkonfigurasjon

    private const string PasswordVariable = "BEREDSKAP_TEST_DB_PASSWORD";

    /*
        Oppretter en tilkoblingsstreng med faste,
        isolerte tilkoblingsverdier.

        Ingen miljøvariabel kan endre server,
        port, database eller brukernavn.

        Returnerer:
        Tilkoblingsstrengen til MariaDB-testdatabasen.
    */
    private static string CreateConnectionString()
    {
        string? password = Environment.GetEnvironmentVariable(PasswordVariable);

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException($"Miljøvariabelen {PasswordVariable} mangler.");
        }

        var connection = new MySqlConnectionStringBuilder
        {
            Server = "127.0.0.1",
            Port = 3308,
            Database = "beredskap_test_db",
            UserID = "beredskap_test_user",
            Password = password
        };

        return connection.ConnectionString;
    }

    /*
        Oppretter EF Core-innstillinger for MariaDB 11.4.

        connectionString:
        Tilkoblingsstrengen til testdatabasen.

        interceptor:
        Valgfri interceptor for synkronisering
        av databaseoperasjoner.

        Returnerer:
        Konfigurerte innstillinger for ApplicationDbContext.
    */
    private static DbContextOptions<ApplicationDbContext> CreateOptions(
        string connectionString,
        IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();

        builder.UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 4, 0)));

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return builder.Options;
    }

    #endregion

    #region Integrasjonstest

    /*
        Verifiserer at to samtidige bekreftelser
        av samme ressurs ikke kan lykkes begge to.

        Testen benytter to forskjellige ApplicationDbContext-
        instanser for å etterligne samtidige forespørsler
        fra separate databaseforbindelser.

        Begge forsøker å bekrefte samme ressurs.

        Forventet resultat:
        - Nøyaktig én bekreftelse lykkes.
        - Den andre forespørselen forblir ubesvart.
        - Ressursen får status Busy.
        - Nøyaktig ett behov får status Assigned.

        Testdata slettes etter gjennomføringen.
    */
    [MariaDbIntegrationFact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentConfirmations_OnlyOneCanSucceed()
    {
        string connectionString = CreateConnectionString();

        var normalOptions = CreateOptions(connectionString);

        string testId = Guid.NewGuid().ToString("N");

        string ownerId = $"owner-{testId}";
        string requesterId = $"requester-{testId}";

        int resourceId = 0;
        int firstNeedId = 0;
        int secondNeedId = 0;
        int firstAssignmentId = 0;
        int secondAssignmentId = 0;

        #region Opprett testdata

        await using (var setup = new ApplicationDbContext(normalOptions))
        {
            /*
                Opprett bare databaseskjemaet.
                Ikke start nettsiden eller demodataseederne.
            */
            await setup.Database.MigrateAsync();

            setup.Users.AddRange(
                new ApplicationUser
                {
                    Id = ownerId,
                    UserName = $"owner-{testId}"
                },
                new ApplicationUser
                {
                    Id = requesterId,
                    UserName = $"requester-{testId}"
                });

            var resource = new Resource
            {
                ResourceName = $"Samtidighetstest {testId}",
                Description = "Ressurs brukt i integrasjonstest.",
                ResourceType = ResourceType.Transport,
                Quantity = 1,
                OwnerUserId = ownerId,
                OwnerEmail = "owner@integration.test",
                Status = ResourceStatus.Available,
                Latitude = 58.15,
                Longitude = 8.00
            };

            var firstNeed = new Need
            {
                Description = "Integrasjonstest - behov A",
                ResourceType = ResourceType.Transport,
                OwnerUserId = requesterId,
                OwnerEmail = "requester@integration.test",
                Status = NeedStatus.UnderReview,
                Latitude = 58.15,
                Longitude = 8.00
            };

            var secondNeed = new Need
            {
                Description = "Integrasjonstest - behov B",
                ResourceType = ResourceType.Transport,
                OwnerUserId = requesterId,
                OwnerEmail = "requester@integration.test",
                Status = NeedStatus.UnderReview,
                Latitude = 58.15,
                Longitude = 8.00
            };

            setup.Resources.Add(resource);
            setup.Needs.AddRange(firstNeed, secondNeed);

            await setup.SaveChangesAsync();

            resourceId = resource.Id;
            firstNeedId = firstNeed.Id;
            secondNeedId = secondNeed.Id;

            var firstAssignment = new NeedAssignment
            {
                NeedId = firstNeedId,
                ResourceId = resourceId,
                RequestedByUserId = requesterId,
                Status = AssignmentStatus.Requested
            };

            var secondAssignment = new NeedAssignment
            {
                NeedId = secondNeedId,
                ResourceId = resourceId,
                RequestedByUserId = requesterId,
                Status = AssignmentStatus.Requested
            };

            setup.NeedAssignments.AddRange(firstAssignment, secondAssignment);

            await setup.SaveChangesAsync();

            firstAssignmentId = firstAssignment.Id;
            secondAssignmentId = secondAssignment.Id;
        }

        #endregion

        try
        {
            #region Kjør samtidige bekreftelser

            /*
                Begge forespørsler skal ha lest
                tildelingen og ressursen før de
                får fortsette mot bekreftelsen.
            */
            var synchronization = new AssignmentReadBarrierInterceptor();

            var concurrentOptions = CreateOptions(connectionString, synchronization);

            await using var firstContext = new ApplicationDbContext(concurrentOptions);

            await using var secondContext = new ApplicationDbContext(concurrentOptions);

            AssignmentService firstService = CreateAssignmentService(firstContext);

            AssignmentService secondService = CreateAssignmentService(secondContext);

            // Separate tjenester og separate DbContexts.
            Task<ServiceResult> firstTask =
                firstService.RespondAsync(firstAssignmentId, ownerId, accept: true);

            Task<ServiceResult> secondTask =
                secondService.RespondAsync(secondAssignmentId, ownerId, accept: true);

            ServiceResult[] results = await Task.WhenAll(firstTask, secondTask);

            /*
                Kontroller at minst to databaseoppslag
                nådde synkroniseringspunktet.

                Interceptoren kan registrere flere treff
                dersom EF Core utfører ekstra SQL-oppslag.
            */
            Assert.True(
                synchronization.Arrivals >= 2,
                "Forventet minst to synkroniserte SQL-oppslag, " +
                $"men registrerte {synchronization.Arrivals}.");

            /*
                Nøyaktig én bekreftelse skal lykkes.

                Bruk Assert.Single med predikat
                for å unngå xUnit2031-advarsler.
            */
            Assert.Single(results, result => result.Success);

            // Nøyaktig én bekreftelse skal avvises.
            Assert.Single(results, result => !result.Success);

            #endregion

            #region Verifiser tilstanden i MariaDB

            await using var verification = new ApplicationDbContext(normalOptions);

            // Ressursen skal være opptatt.
            Resource resource = await verification.Resources
                .AsNoTracking()
                .SingleAsync(r => r.Id == resourceId);

            Assert.Equal(ResourceStatus.Busy, resource.Status);

            // Hent begge forespørslene.
            int[] assignmentIds =
            [
                firstAssignmentId,
                secondAssignmentId
            ];

            List<NeedAssignment> assignments =
                await verification.NeedAssignments
                    .AsNoTracking()
                    .Where(a => assignmentIds.Contains(a.Id))
                    .ToListAsync();

            Assert.Equal(2, assignments.Count);

            // Bare én forespørsel kan være bekreftet.
            Assert.Single(assignments, a => a.Status == AssignmentStatus.Confirmed);

            // Den andre skal fortsatt vente på svar.
            Assert.Single(assignments, a => a.Status == AssignmentStatus.Requested);

            // Hent begge behovene.
            int[] needIds =
            [
                firstNeedId,
                secondNeedId
            ];

            List<Need> needs = await verification.Needs
                .AsNoTracking()
                .Where(n => needIds.Contains(n.Id))
                .ToListAsync();

            Assert.Equal(2, needs.Count);

            // Bare ett behov skal være tildelt.
            Assert.Single(needs, n => n.Status == NeedStatus.Assigned);

            // Det andre behovet skal fortsatt vurderes.
            Assert.Single(needs, n => n.Status == NeedStatus.UnderReview);

            #endregion
        }
        finally
        {
            #region Rydd opp i testdata

            /*
                Slett bare data opprettet i denne testen.
                Databaseskjemaet og øvrige data beholdes.
            */
            await using var cleanup = new ApplicationDbContext(normalOptions);

            int[] assignmentIds =
            [
                firstAssignmentId,
                secondAssignmentId
            ];

            // Slett forespørslene først.
            var assignments = await cleanup.NeedAssignments
                .Where(a => assignmentIds.Contains(a.Id))
                .ToListAsync();

            cleanup.NeedAssignments.RemoveRange(assignments);

            // Fjern varsler som testen har opprettet.
            var notifications = await cleanup.Notifications
                .Where(n => n.UserId == ownerId || n.UserId == requesterId)
                .ToListAsync();

            cleanup.Notifications.RemoveRange(notifications);

            // Fjern testbehovene.
            int[] needIds =
            [
                firstNeedId,
                secondNeedId
            ];

            var needs = await cleanup.Needs
                .Where(n => needIds.Contains(n.Id))
                .ToListAsync();

            cleanup.Needs.RemoveRange(needs);

            // Fjern testressursen.
            var resource = await cleanup.Resources
                .FirstOrDefaultAsync(r => r.Id == resourceId);

            if (resource is not null)
            {
                cleanup.Resources.Remove(resource);
            }

            // Fjern testbrukerne.
            var users = await cleanup.Users
                .Where(u => u.Id == ownerId || u.Id == requesterId)
                .ToListAsync();

            cleanup.Users.RemoveRange(users);

            await cleanup.SaveChangesAsync();

            #endregion
        }
    }

    #endregion

    #region Testavhengigheter

    /*
        Oppretter den ekte AssignmentService.

        E-post og SMS erstattes av strenge mocks.
        Testbrukerne har ingen e-postadresse eller
        telefonnummer, så ingen eksterne meldinger sendes.

        context:
        Databasekonteksten for denne testoperasjonen.

        Returnerer:
        En AssignmentService med isolerte testavhengigheter.
    */
    private static AssignmentService CreateAssignmentService(ApplicationDbContext context)
    {
        var emailSender = new Mock<IPortalEmailSender>(MockBehavior.Strict);

        var smsSender = new Mock<ISmsSender>(MockBehavior.Strict);

        var configuration = new ConfigurationBuilder().Build();

        var notifications = new NotificationService(
            context,
            emailSender.Object,
            smsSender.Object,
            configuration,
            NullLogger<NotificationService>.Instance);

        return new AssignmentService(context, notifications);
    }

    #endregion

    #region Synkronisering av samtidige operasjoner

    /*
        Synkroniserer de to første databaseoppslagene
        som henter en forespørsel med tilhørende
        behov og ressurs.

        Dermed får begge operasjoner mulighet til
        å observere ressursen som tilgjengelig før
        bekreftelsene fortsetter.

        Brukes kun i denne integrasjonstesten.
    */
    private sealed class AssignmentReadBarrierInterceptor
        : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> _bothReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _arrivals;

        /*
            Antall relevante SQL-oppslag registrert
            av interceptoren.
        */
        public int Arrivals => Volatile.Read(ref _arrivals);

        /*
            Holder de første relevante SQL-oppslagene
            tilbake til begge operasjonene er klare.
        */
        public override async ValueTask<DbDataReader>
            ReaderExecutedAsync(
                DbCommand command,
                CommandExecutedEventData eventData,
                DbDataReader result,
                CancellationToken cancellationToken = default)
        {
            string sql = command.CommandText;

            bool isAssignmentLookup =
                sql.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("NeedAssignments", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("Resources", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("Needs", StringComparison.OrdinalIgnoreCase);

            if (isAssignmentLookup)
            {
                int arrivals = Interlocked.Increment(ref _arrivals);

                if (arrivals == 2)
                {
                    _bothReady.TrySetResult(true);
                }

                /*
                    Hindrer at en hengende test
                    venter uendelig.
                */
                await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return result;
        }
    }

    #endregion
}

/*
    Aktiverer MariaDB-integrasjonstester bare
    når de er eksplisitt slått på.

    Vanlige enhetstester trenger dermed ikke Docker.
*/
public sealed class MariaDbIntegrationFactAttribute : FactAttribute
{
    /*
        Kontrollerer om MariaDB-integrasjonstester
        er aktivert gjennom en miljøvariabel.
    */
    public MariaDbIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BEREDSKAP_RUN_MARIADB_TESTS") != "1")
        {
            Skip =
                "MariaDB-integrasjonstest. " +
                "Sett BEREDSKAP_RUN_MARIADB_TESTS=1 " +
                "for å kjøre testen.";
        }
    }
}
