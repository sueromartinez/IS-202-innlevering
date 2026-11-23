
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

/// <summary>
/// Integrasjonstester for samtidige kanselleringer
/// og bekreftelser av ressursforespørsler.
/// </summary>
/// <remarks>
/// Testene bruker utelukkende:
/// 127.0.0.1:3308 / beredskap_test_db.
///
/// Integrasjonstester må aktiveres eksplisitt med:
/// BEREDSKAP_RUN_MARIADB_TESTS=1.
///
/// Databasepassordet leses fra:
/// BEREDSKAP_TEST_DB_PASSWORD.
///
/// Eksisterende utviklingsdatabase påvirkes ikke.
/// </remarks>
public class CancellationConcurrencyIntegrationTests
{
    #region Databasekonfigurasjon

    private const string PasswordVariable =
        "BEREDSKAP_TEST_DB_PASSWORD";

    /// <summary>
    /// Oppretter en tilkobling utelukkende til
    /// den isolerte MariaDB-testdatabasen.
    /// </summary>
    private static string CreateConnectionString()
    {
        string? password =
            Environment.GetEnvironmentVariable(PasswordVariable);

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"Miljøvariabelen {PasswordVariable} mangler.");
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

    /// <summary>
    /// Konfigurerer EF Core med samme MariaDB-versjon
    /// som hovedapplikasjonen.
    /// </summary>
    private static DbContextOptions<ApplicationDbContext> CreateOptions(
        string connectionString,
        IInterceptor? interceptor = null)
    {
        var builder =
            new DbContextOptionsBuilder<ApplicationDbContext>();

        builder.UseMySql(
            connectionString,
            new MariaDbServerVersion(new Version(11, 4, 0)));

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return builder.Options;
    }

    #endregion

    #region Samtidighetstest

    /// <summary>
    /// Kontrollerer at samtidig kansellering og bekreftelse
    /// ikke etterlater en inkonsistent ressursreservasjon.
    /// </summary>
    /// <remarks>
    /// Testen setter kanselleringen på pause rett før
    /// dens endringer lagres i databasen.
    ///
    /// Bekreftelsen får deretter fullføre først.
    ///
    /// Når kanselleringen fortsetter, skal den ikke
    /// kunne overskrive en allerede bekreftet tildeling
    /// uten korrekt håndtering av ressursstatus.
    ///
    /// Forventningen er at bare én av de konkurrerende
    /// handlingene skal lykkes, og at databasen
    /// skal ha konsistente sluttstatuser.
    ///
    /// Testen er ment å avdekke en mulig feil
    /// i den nåværende implementasjonen.
    /// </remarks>
    [MariaDbIntegrationFact]
    [Trait("Category", "Integration")]
    public async Task CancelAndConfirmSameRequest_CannotCauseInconsistentBooking()
    {
        string connectionString = CreateConnectionString();

        var options = CreateOptions(connectionString);

        string testId = Guid.NewGuid().ToString("N");

        string ownerId = $"owner-cancel-{testId}";
        string requesterId = $"requester-cancel-{testId}";

        int resourceId = 0;
        int needId = 0;
        int assignmentId = 0;

        #region Opprett testdata

        await using (var setup = new ApplicationDbContext(options))
        {
            await setup.Database.MigrateAsync();

            setup.Users.AddRange(
                new ApplicationUser
                {
                    Id = ownerId,
                    UserName = $"owner-cancel-{testId}"
                },
                new ApplicationUser
                {
                    Id = requesterId,
                    UserName = $"requester-cancel-{testId}"
                });

            var resource = new Resource
            {
                ResourceName = $"Kanselleringstest {testId}",
                Description = "Ressurs for samtidighetstesting.",
                ResourceType = ResourceType.Transport,
                Quantity = 1,
                OwnerUserId = ownerId,
                OwnerEmail = "owner@integration.test",
                Status = ResourceStatus.Available,
                Latitude = 58.15,
                Longitude = 8.00
            };

            var need = new Need
            {
                Description = "Test av samtidig kansellering.",
                ResourceType = ResourceType.Transport,
                OwnerUserId = requesterId,
                OwnerEmail = "requester@integration.test",
                Status = NeedStatus.UnderReview,
                Latitude = 58.15,
                Longitude = 8.00
            };

            setup.Resources.Add(resource);
            setup.Needs.Add(need);

            await setup.SaveChangesAsync();

            resourceId = resource.Id;
            needId = need.Id;

            var assignment = new NeedAssignment
            {
                NeedId = needId,
                ResourceId = resourceId,
                RequestedByUserId = requesterId,
                Status = AssignmentStatus.Requested
            };

            setup.NeedAssignments.Add(assignment);

            await setup.SaveChangesAsync();

            assignmentId = assignment.Id;
        }

        #endregion

        try
        {
            #region Gjennomfør konkurrerende operasjoner

            // Setter kanselleringen på pause
            // etter at den har lest forespørselen,
            // men før SaveChangesAsync lagrer endringene.
            var pause =
                new PauseBeforeCancellationSaveInterceptor();

            var cancellationOptions =
                CreateOptions(connectionString, pause);

            await using var cancellationContext =
                new ApplicationDbContext(cancellationOptions);

            await using var confirmationContext =
                new ApplicationDbContext(options);

            AssignmentService cancellationService =
                CreateAssignmentService(cancellationContext);

            AssignmentService confirmationService =
                CreateAssignmentService(confirmationContext);

            // Start kanselleringen først.
            Task<ServiceResult> cancellationTask =
                cancellationService.CancelAsync(assignmentId);

            ServiceResult confirmationResult;

            try
            {
                // Vent til kanselleringen er klar
                // til å skrive til databasen.
                await pause.WaitUntilPausedAsync();

                // Bekreft samme forespørsel mens
                // kanselleringens endringer ennå
                // ikke er lagret.
                confirmationResult =
                    await confirmationService.RespondAsync(
                        assignmentId,
                        ownerId,
                        accept: true);
            }
            finally
            {
                // Slipp alltid kanselleringen videre,
                // også dersom bekreftelsen feiler.
                pause.Release();

                await cancellationTask;
            }

            ServiceResult cancellationResult =
                await cancellationTask;

            #endregion

            #region Kontroller databasekonsistens

            await using var verification =
                new ApplicationDbContext(options);

            NeedAssignment finalAssignment =
                await verification.NeedAssignments
                    .AsNoTracking()
                    .SingleAsync(a => a.Id == assignmentId);

            Resource finalResource =
                await verification.Resources
                    .AsNoTracking()
                    .SingleAsync(r => r.Id == resourceId);

            Need finalNeed =
                await verification.Needs
                    .AsNoTracking()
                    .SingleAsync(n => n.Id == needId);

            // Gyldig resultat A:
            // Bekreftelsen vant.
            bool confirmationWon =
                finalAssignment.Status == AssignmentStatus.Confirmed
                && finalResource.Status == ResourceStatus.Busy
                && finalNeed.Status == NeedStatus.Assigned;

            // Gyldig resultat B:
            // Kanselleringen vant.
            bool cancellationWon =
                finalAssignment.Status == AssignmentStatus.Cancelled
                && finalResource.Status == ResourceStatus.Available
                && finalNeed.Status == NeedStatus.New;

            // Databasen må ende i én konsistent tilstand.
            Assert.True(
                confirmationWon || cancellationWon,
                "Inkonsistent booking etter samtidige handlinger. " +
                $"Forespørsel: {finalAssignment.Status}, " +
                $"Ressurs: {finalResource.Status}, " +
                $"Behov: {finalNeed.Status}.");

            // Begge konkurrerende handlinger skal ikke
            // rapportere at de lykkes med samme forespørsel.
            Assert.True(
                confirmationResult.Success != cancellationResult.Success,
                "Både bekreftelse og kansellering rapporterte " +
                "samme suksessresultat. " +
                $"Bekreftelse: {confirmationResult.Success}, " +
                $"Kansellering: {cancellationResult.Success}.");

            #endregion
        }
        finally
        {
            #region Rydd opp i testdata

            // Fjerner bare data med denne testens ID-er.
            // Ingen database eller Docker-volumer slettes.
            await using var cleanup =
                new ApplicationDbContext(options);

            var assignments =
                await cleanup.NeedAssignments
                    .Where(a => a.Id == assignmentId)
                    .ToListAsync();

            cleanup.NeedAssignments.RemoveRange(assignments);

            var notifications =
                await cleanup.Notifications
                    .Where(n =>
                        n.UserId == ownerId ||
                        n.UserId == requesterId)
                    .ToListAsync();

            cleanup.Notifications.RemoveRange(notifications);

            var needs =
                await cleanup.Needs
                    .Where(n => n.Id == needId)
                    .ToListAsync();

            cleanup.Needs.RemoveRange(needs);

            var resources =
                await cleanup.Resources
                    .Where(r => r.Id == resourceId)
                    .ToListAsync();

            cleanup.Resources.RemoveRange(resources);

            var users =
                await cleanup.Users
                    .Where(u =>
                        u.Id == ownerId ||
                        u.Id == requesterId)
                    .ToListAsync();

            cleanup.Users.RemoveRange(users);

            await cleanup.SaveChangesAsync();

            #endregion
        }
    }

    #endregion

    #region Testavhengigheter

    /// <summary>
    /// Oppretter den ekte AssignmentService uten
    /// å sende eksterne meldinger.
    /// </summary>
    private static AssignmentService CreateAssignmentService(
        ApplicationDbContext context)
    {
        var emailSender =
            new Mock<IPortalEmailSender>(MockBehavior.Strict);

        var smsSender =
            new Mock<ISmsSender>(MockBehavior.Strict);

        IConfiguration configuration =
            new ConfigurationBuilder().Build();

        var notifications = new NotificationService(
            context,
            emailSender.Object,
            smsSender.Object,
            configuration,
            NullLogger<NotificationService>.Instance);

        return new AssignmentService(
            context,
            notifications);
    }

    #endregion

    #region Synkronisering

    /// <summary>
    /// Holder kanselleringens første SaveChangesAsync
    /// tilbake inntil testen tillater lagring.
    /// </summary>
    /// <remarks>
    /// Ved å bruke en SaveChangesInterceptor
    /// vet vi at kanselleringen allerede har lest
    /// forespørselen og bestemt hvilken status
    /// som skal lagres.
    ///
    /// Dette gjør testen mer målrettet enn
    /// å forsøke å treffe en tilfeldig timing.
    /// </remarks>
    private sealed class PauseBeforeCancellationSaveInterceptor
        : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource<bool> _paused =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _hasPaused;

        /// <summary>
        /// Venter på at kanselleringen skal nå
        /// lagringspunktet.
        /// </summary>
        public Task WaitUntilPausedAsync()
        {
            return _paused.Task.WaitAsync(
                TimeSpan.FromSeconds(20));
        }

        /// <summary>
        /// Lar kanselleringen fortsette.
        /// </summary>
        public void Release()
        {
            _release.TrySetResult(true);
        }

        /// <inheritdoc />
        public override async ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(
                    ref _hasPaused,
                    1,
                    0) == 0)
            {
                _paused.TrySetResult(true);

                await _release.Task.WaitAsync(
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
            }

            return result;
        }
    }

    #endregion
}
