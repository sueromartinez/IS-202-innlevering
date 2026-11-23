
using System.Security.Claims;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Matching;
using FirstWebAppInDocker.Services.Notifications;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FirstWebAppInDocker.Tests;

/// <summary>
/// Tester eierskapskontroller og autorisasjon i MatchController,
/// samt kansellering og frigjøring av ressurser.
/// </summary>
/// <remarks>
/// Testene bruker EF Core InMemory og ekte applikasjonstjenester
/// med testavhengigheter.
///
/// Controllertestene kontrollerer autorisasjonslogikken inne
/// i handlingene, men ikke ASP.NET Core sitt
/// autorisasjonsmiddleware.
///
/// Kanselleringstestene kontrollerer samspillet mellom
/// AssignmentService og ResourceMatchingService.
/// </remarks>
public class MatchControllerTests
{
    #region Testoppsett

    /// <summary>
    /// Oppretter en isolert InMemory-database.
    /// </summary>
    /// <returns>En ny databasekontekst.</returns>
    private static ApplicationDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Oppretter et behov med en bestemt eier.
    /// </summary>
    /// <param name="database">Testdatabasen.</param>
    /// <param name="ownerUserId">Behovets eier.</param>
    /// <returns>Det lagrede behovet.</returns>
    private static Need AddNeed(
        ApplicationDbContext database,
        string ownerUserId)
    {
        var need = new Need
        {
            OwnerUserId = ownerUserId,
            OwnerEmail = $"{ownerUserId}@example.com",
            ResourceType = ResourceType.Boat,
            Priority = NeedPriority.Acute,
            Description = "Testbehov",
            Quantity = 1,
            Position = new Coordinate(58.3405, 8.5934),
            Status = NeedStatus.New
        };

        database.Needs.Add(need);
        database.SaveChanges();

        return need;
    }

    /// <summary>
    /// Oppretter en tilgjengelig testressurs.
    /// </summary>
    /// <param name="database">Testdatabasen.</param>
    /// <returns>Den lagrede ressursen.</returns>
    private static Resource AddResource(
        ApplicationDbContext database)
    {
        var resource = new Resource
        {
            ResourceName = "Testbåt",
            Description = "Båt for beredskap",
            ResourceType = ResourceType.Boat,
            Quantity = 1,
            Position = new Coordinate(58.3405, 8.5934),
            OwnerUserId = "provider-1",
            OwnerEmail = "provider@example.com",
            Status = ResourceStatus.Available
        };

        database.Resources.Add(resource);
        database.SaveChanges();

        return resource;
    }

    /// <summary>
    /// Oppretter en aktiv forespørsel knyttet til et behov.
    /// </summary>
    /// <param name="database">Testdatabasen.</param>
    /// <param name="need">Behovet.</param>
    /// <param name="resource">Ressursen.</param>
    /// <returns>Den lagrede forespørselen.</returns>
    private static NeedAssignment AddAssignment(
        ApplicationDbContext database,
        Need need,
        Resource resource)
    {
        var assignment = new NeedAssignment
        {
            NeedId = need.Id,
            ResourceId = resource.Id,
            RequestedByUserId = need.OwnerUserId!,
            Status = AssignmentStatus.Requested,
            CreatedAt = DateTime.UtcNow
        };

        need.Status = NeedStatus.UnderReview;

        database.NeedAssignments.Add(assignment);
        database.SaveChanges();

        return assignment;
    }

    /// <summary>
    /// Testimplementasjon av e-posttjenesten.
    /// </summary>
    private sealed class FakeEmailSender : IPortalEmailSender
    {
        /// <inheritdoc />
        public Task SendAsync(
            string toEmail,
            string subject,
            string body)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Testimplementasjon av SMS-tjenesten.
    /// </summary>
    private sealed class FakeSmsSender : ISmsSender
    {
        /// <inheritdoc />
        public Task SendAsync(
            string phoneNumber,
            string message)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Testimplementasjon av TempData som ikke
    /// trenger session eller cookies.
    /// </summary>
    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        /// <inheritdoc />
        public IDictionary<string, object> LoadTempData(
            HttpContext context)
        {
            return new Dictionary<string, object>();
        }

        /// <inheritdoc />
        public void SaveTempData(
            HttpContext context,
            IDictionary<string, object> values)
        {
            // Ingen ekstern lagring i testene.
        }
    }

    /// <summary>
    /// Oppretter en MatchController med en simulert
    /// innlogget bruker og en bestemt rolle.
    /// </summary>
    /// <remarks>
    /// ControllerContext får både HttpContext, RouteData
    /// og ControllerActionDescriptor, slik at MVC-funksjoner
    /// som RedirectToAction kan brukes i enhetstester.
    /// </remarks>
    /// <param name="database">Testdatabasen.</param>
    /// <param name="userId">Innlogget brukers Identity-ID.</param>
    /// <param name="role">Brukerens rolle.</param>
    /// <returns>En konfigurert MatchController.</returns>
    private static MatchController CreateController(
        ApplicationDbContext database,
        string userId,
        string role = IdentitySeeder.PublicActorRole)
    {
        var serviceProvider =
            new ServiceCollection().BuildServiceProvider();

        var userManager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser>(database),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            serviceProvider,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var notifications = new NotificationService(
            database,
            new FakeEmailSender(),
            new FakeSmsSender(),
            new ConfigurationBuilder().Build(),
            NullLogger<NotificationService>.Instance);

        var assignments = new AssignmentService(
            database,
            notifications);

        var matching = new ResourceMatchingService(
            new EfResourceMatchingRepository(database));

        var controller = new MatchController(
            database,
            matching,
            assignments,
            userManager);

        // Simuler innlogging med korrekt Identity-ID og rolle.
        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                userId),

            new Claim(
                ClaimTypes.Role,
                role)
        };

        var identity = new ClaimsIdentity(
            claims,
            authenticationType: "TestAuthentication");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        // VIKTIG RETTELSE:
        //
        // ControllerContext.ActionDescriptor krever
        // ControllerActionDescriptor, ikke ActionDescriptor.
        //
        // RouteData og ActionDescriptor må begge være satt
        // for at MVC skal kunne bruke RedirectToAction().
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new ControllerActionDescriptor()
        };

        return controller;
    }

    #endregion

    #region Autorisasjon ved kansellering

    /// <summary>
    /// Kontrollerer at en offentlig aktør ikke kan
    /// avbryte forespørsler som tilhører andre aktører.
    /// </summary>
    [Fact]
    public async Task Cancel_NonOwner_ReturnsForbid()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(
            database,
            "actor-owner");

        Resource resource = AddResource(database);

        NeedAssignment assignment = AddAssignment(
            database,
            need,
            resource);

        var controller = CreateController(
            database,
            "actor-other");

        IActionResult result = await controller.Cancel(
            assignment.Id,
            need.Id);

        Assert.IsType<ForbidResult>(result);

        Assert.Equal(
            AssignmentStatus.Requested,
            database.NeedAssignments.Single().Status);

        Assert.Equal(
            NeedStatus.UnderReview,
            database.Needs.Single().Status);
    }

    /// <summary>
    /// Kontrollerer at feil behovsidentifikator
    /// ikke kan brukes til å kansellere en forespørsel.
    /// </summary>
    [Fact]
    public async Task Cancel_WrongNeedId_ReturnsNotFound()
    {
        using var database = CreateDatabase();

        Need firstNeed = AddNeed(
            database,
            "actor-owner");

        Need secondNeed = AddNeed(
            database,
            "actor-owner");

        Resource resource = AddResource(database);

        NeedAssignment assignment = AddAssignment(
            database,
            firstNeed,
            resource);

        var controller = CreateController(
            database,
            "actor-owner");

        IActionResult result = await controller.Cancel(
            assignment.Id,
            secondNeed.Id);

        Assert.IsType<NotFoundResult>(result);

        Assert.Equal(
            AssignmentStatus.Requested,
            database.NeedAssignments.Single().Status);
    }

    /// <summary>
    /// Kontrollerer at behovets eier kan kansellere en
    /// bekreftet forespørsel gjennom MatchController.
    /// </summary>
    /// <remarks>
    /// Etter kansellering skal ressursen være tilgjengelig
    /// igjen, og behovets status skal oppdateres.
    /// </remarks>
    [Fact]
    public async Task Cancel_Owner_CancelsConfirmedAssignment()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(
            database,
            "actor-owner");

        Resource resource = AddResource(database);

        NeedAssignment assignment = AddAssignment(
            database,
            need,
            resource);

        // Simuler en bekreftet tildeling.
        assignment.Status = AssignmentStatus.Confirmed;
        resource.Status = ResourceStatus.Busy;
        need.Status = NeedStatus.Assigned;

        database.SaveChanges();

        var controller = CreateController(
            database,
            "actor-owner");

        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            new FakeTempDataProvider());

        // Registrer nødvendige tjenester for audit og MVC.
        // RouteData og ControllerActionDescriptor er
        // allerede satt i CreateController().
        controller.HttpContext.RequestServices =
            new ServiceCollection()
                .AddSingleton<ApplicationDbContext>(database)
                .AddSingleton<IUrlHelperFactory, UrlHelperFactory>()
                .AddLogging()
                .BuildServiceProvider();

        // Utfør kanselleringen gjennom controlleren.
        IActionResult result = await controller.Cancel(
            assignment.Id,
            need.Id);

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal(
            "Need",
            redirect.ActionName);

        Assert.Equal(
            need.Id,
            redirect.RouteValues!["id"]);

        // Forespørselen beholdes som historikk.
        Assert.Equal(
            AssignmentStatus.Cancelled,
            database.NeedAssignments.Single().Status);

        // Ressursen skal bli ledig igjen.
        Assert.Equal(
            ResourceStatus.Available,
            database.Resources.Single().Status);

        // Ingen aktive forespørsler gjenstår.
        Assert.Equal(
            NeedStatus.New,
            database.Needs.Single().Status);

        // Controlleren skal rapportere vellykket kansellering.
        Assert.Equal(
            "Forespørselen er avbrutt.",
            controller.TempData["MatchMessage"]);
    }

    #endregion

    #region Autorisasjon ved sending av forespørsel

    /// <summary>
    /// Kontrollerer at en offentlig aktør ikke kan sende
    /// forespørsler på vegne av en annen offentlig aktør.
    /// </summary>
    [Fact]
    public async Task SendRequest_NonOwner_ReturnsForbid()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(
            database,
            "actor-owner");

        Resource resource = AddResource(database);

        var controller = CreateController(
            database,
            "actor-other");

        IActionResult result = await controller.SendRequest(
            need.Id,
            resource.Id,
            "Jeg ønsker denne ressursen.");

        Assert.IsType<ForbidResult>(result);

        Assert.Empty(database.NeedAssignments);

        Assert.Equal(
            NeedStatus.New,
            database.Needs.Single().Status);
    }

    /// <summary>
    /// Kontrollerer at behovets eier passerer
    /// eierskapskontrollen i SendRequest.
    /// </summary>
    /// <remarks>
    /// En ikke-eksisterende ressurs brukes for å
    /// isolere eierskapskontrollen fra ressursvalideringen.
    /// </remarks>
    [Fact]
    public async Task SendRequest_Owner_PassesOwnershipCheck()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(
            database,
            "actor-owner");

        var controller = CreateController(
            database,
            "actor-owner");

        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            new FakeTempDataProvider());

        IActionResult result = await controller.SendRequest(
            need.Id,
            resourceId: 999999,
            message: null);

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal(
            "Need",
            redirect.ActionName);

        // Eieren skal nå ressursvalideringen,
        // og ikke bli avvist med 403 Forbidden.
        Assert.Equal(
            "Fant ikke ressursen.",
            controller.TempData["MatchError"]);

        Assert.Empty(database.NeedAssignments);

        Assert.Equal(
            NeedStatus.New,
            database.Needs.Single().Status);
    }

    /// <summary>
    /// Kontrollerer at administratoren kan passere
    /// eierskapskontrollen for andre aktørers behov.
    /// </summary>
    [Fact]
    public async Task SendRequest_Admin_PassesOwnershipCheck()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(
            database,
            "actor-owner");

        var controller = CreateController(
            database,
            "admin-user",
            IdentitySeeder.AdminRole);

        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            new FakeTempDataProvider());

        IActionResult result = await controller.SendRequest(
            need.Id,
            resourceId: 999999,
            message: null);

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal(
            "Need",
            redirect.ActionName);

        // Administratoren skal passere eierskapskontrollen.
        Assert.Equal(
            "Fant ikke ressursen.",
            controller.TempData["MatchError"]);

        Assert.Empty(database.NeedAssignments);

        Assert.Equal(
            NeedStatus.New,
            database.Needs.Single().Status);
    }

    #endregion

    #region Autorisasjon ved avslutning av behov

    /// <summary>
    /// Kontrollerer at en offentlig aktør ikke kan
    /// markere en annen aktørs behov som løst.
    /// </summary>
    [Fact]
    public async Task Resolve_NonOwner_ReturnsForbid()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(
            database,
            "actor-owner");

        var controller = CreateController(
            database,
            "actor-other");

        IActionResult result =
            await controller.Resolve(need.Id);

        Assert.IsType<ForbidResult>(result);

        Assert.Equal(
            NeedStatus.New,
            database.Needs.Single().Status);
    }

    #endregion

    #region Kansellering og ny matching

    /// <summary>
    /// Tester at en bekreftet forespørsel kan kanselleres,
    /// at ressursen frigjøres, og at den kan matches
    /// og forespørres på nytt.
    /// </summary>
    /// <remarks>
    /// Testen bruker AssignmentService og ResourceMatchingService.
    /// Autorisasjon gjennom MatchController testes separat.
    /// </remarks>
    [Fact]
    public async Task CancelConfirmedAssignment_ReleasesResourceAndAllowsNewRequest()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(
            database,
            "actor-owner");

        Resource resource = AddResource(database);

        var notifications = new NotificationService(
            database,
            new FakeEmailSender(),
            new FakeSmsSender(),
            new ConfigurationBuilder().Build(),
            NullLogger<NotificationService>.Instance);

        var assignments = new AssignmentService(
            database,
            notifications);

        var matching = new ResourceMatchingService(
            new EfResourceMatchingRepository(database));

        // =====================================================
        // STEG 1: Send forespørsel.
        // =====================================================

        ServiceResult requested =
            await assignments.RequestAsync(
                need.Id,
                resource.Id,
                "actor-owner",
                null,
                false);

        Assert.True(requested.Success);

        Assert.Single(database.NeedAssignments);

        int assignmentId =
            database.NeedAssignments.Single().Id;

        Assert.Equal(
            AssignmentStatus.Requested,
            database.NeedAssignments.Single().Status);

        Assert.Equal(
            NeedStatus.UnderReview,
            database.Needs.Single().Status);

        // =====================================================
        // STEG 2: Ressursleverandøren bekrefter.
        // =====================================================

        ServiceResult confirmed =
            await assignments.RespondAsync(
                assignmentId,
                "provider-1",
                accept: true);

        Assert.True(confirmed.Success);

        Assert.Equal(
            AssignmentStatus.Confirmed,
            database.NeedAssignments.Single().Status);

        Assert.Equal(
            ResourceStatus.Busy,
            database.Resources.Single().Status);

        Assert.Equal(
            NeedStatus.Assigned,
            database.Needs.Single().Status);

        // =====================================================
        // STEG 3: Opptatt ressurs skal ikke kunne matches.
        // =====================================================

        var beforeCancellation =
            await matching.FindNearbyResourcesAsync(
                need.Latitude,
                need.Longitude,
                radiusKm: 50,
                types: new[] { ResourceType.Boat },
                excludeRequestedForNeedId: need.Id);

        Assert.Empty(beforeCancellation);

        // =====================================================
        // STEG 4: Avbryt den bekreftede forespørselen.
        // =====================================================

        ServiceResult cancelled =
            await assignments.CancelAsync(assignmentId);

        Assert.True(cancelled.Success);

        Assert.Equal(
            AssignmentStatus.Cancelled,
            database.NeedAssignments.Single().Status);

        Assert.Equal(
            ResourceStatus.Available,
            database.Resources.Single().Status);

        Assert.Equal(
            NeedStatus.New,
            database.Needs.Single().Status);

        // =====================================================
        // STEG 5: Ressursen skal kunne matches på nytt.
        // =====================================================

        var afterCancellation =
            await matching.FindNearbyResourcesAsync(
                need.Latitude,
                need.Longitude,
                radiusKm: 50,
                types: new[] { ResourceType.Boat },
                excludeRequestedForNeedId: need.Id);

        Assert.Single(afterCancellation);

        Assert.Equal(
            resource.Id,
            afterCancellation[0].ResourceId);

        // =====================================================
        // STEG 6: Send en ny forespørsel.
        // =====================================================

        ServiceResult requestedAgain =
            await assignments.RequestAsync(
                need.Id,
                resource.Id,
                "actor-owner",
                null,
                false);

        Assert.True(requestedAgain.Success);

        // Den kansellerte forespørselen beholdes,
        // mens en ny aktiv forespørsel opprettes.
        Assert.Equal(
            2,
            database.NeedAssignments.Count());

        Assert.Single(
            database.NeedAssignments.Where(assignment =>
                assignment.Status == AssignmentStatus.Cancelled));

        Assert.Single(
            database.NeedAssignments.Where(assignment =>
                assignment.Status == AssignmentStatus.Requested));

        Assert.Equal(
            NeedStatus.UnderReview,
            database.Needs.Single().Status);

        // Ressursen blir ikke opptatt før
        // den nye forespørselen bekreftes.
        Assert.Equal(
            ResourceStatus.Available,
            database.Resources.Single().Status);
    }

    #endregion
}
