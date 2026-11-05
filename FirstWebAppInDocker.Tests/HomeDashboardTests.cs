
using System.Reflection;
using System.Security.Claims;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class HomeDashboardTests
{
    // ========================================
    // HJELPER - MIDLERTIDIG TESTDATABASE
    // ========================================

    private static ApplicationDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new ApplicationDbContext(options);
    }


    // ========================================
    // HJELPER - TESTBRUKER
    // ========================================

    private static ApplicationUser CreateUser(
        string id = "user-123",
        string email = "ola@example.com",
        string name = "Ola Nordmann")
    {
        return new ApplicationUser
        {
            Id = id,
            Email = email,
            UserName = email,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };
    }


    // ========================================
    // HJELPER - TESTRESSURS
    // ========================================

    private static Resource CreateResource(
        string name,
        string? ownerUserId,
        string ownerEmail,
        string? availability = "Tilgjengelig",
        DateTime? createdAt = null)
    {
        return new Resource
        {
            ResourceName = name,
            ResourceType = ResourceType.Generator,
            Description = "Ressurs for beredskap",
            Quantity = 1,
            Availability = availability,

            Latitude = 58.1467,
            Longitude = 7.9956,

            OwnerUserId = ownerUserId,
            OwnerEmail = ownerEmail,

            CreatedAt = createdAt ?? DateTime.UtcNow
        };
    }


    // ========================================
    // HJELPER - TESTBEHOV
    // ========================================

    private static Need CreateNeed(
        string description,
        string ownerEmail,
        DateTime createdAt)
    {
        return new Need
        {
            ResourceType = ResourceType.Generator,
            Priority = NeedPriority.Acute,
            Quantity = 2,
            Description = description,

            Latitude = 58.1467,
            Longitude = 7.9956,

            OwnerEmail = ownerEmail,
            CreatedAt = createdAt
        };
    }


    // ========================================
    // HJELPER - CONTROLLER MED IDENTITY
    // ========================================

    private static HomeController CreateController(
        ApplicationDbContext database,
        ApplicationUser? currentUser,
        params string[] roles)
    {
        var store =
            new Mock<IUserStore<ApplicationUser>>();

        var userManager =
            new Mock<UserManager<ApplicationUser>>(
                store.Object,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!
            );

        userManager
            .Setup(manager =>
                manager.GetUserAsync(
                    It.IsAny<ClaimsPrincipal>()
                )
            )
            .ReturnsAsync(currentUser);

        var claims = new List<Claim>();

        if (currentUser != null)
        {
            claims.Add(
                new Claim(
                    ClaimTypes.NameIdentifier,
                    currentUser.Id
                )
            );
        }

        foreach (string role in roles)
        {
            claims.Add(
                new Claim(
                    ClaimTypes.Role,
                    role
                )
            );
        }

        var identity = new ClaimsIdentity(
            claims,
            currentUser == null
                ? null
                : "TestAuthentication"
        );

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        return new HomeController(
            database,
            userManager.Object
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }


    // ========================================
    // HJELPER - HENT DASHBOARDMODELL
    // ========================================

    private static DashboardViewModel GetDashboardModel(
        IActionResult result)
    {
        var view = Assert.IsType<ViewResult>(result);

        return Assert.IsType<DashboardViewModel>(
            view.Model
        );
    }


    // ========================================
    // TEST 1
    // FORSIDEN ER TILGJENGELIG
    // ========================================

    [Fact]
    public void Index_ShouldReturnView()
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            null
        );

        var result = controller.Index();

        Assert.IsType<ViewResult>(result);
    }


    // ========================================
    // TEST 2
    // MANGLENDE IDENTITY-BRUKER AVVISES
    // ========================================

    [Fact]
    public async Task Dashboard_WithoutIdentityUser_ShouldChallenge()
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            null
        );

        var result = await controller.Dashboard();

        Assert.IsType<ChallengeResult>(result);
    }


    // ========================================
    // TEST 3
    // RESSURSLEVERANDØR SER BARE EGNE RESSURSER
    // ========================================

    [Fact]
    public async Task Dashboard_ResourceProvider_ShouldOnlySeeOwnResources()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        database.Resources.AddRange(
            CreateResource(
                "Mitt aggregat",
                user.Id,
                user.Email!
            ),

            // Eldre ressurs uten OwnerUserId.
            CreateResource(
                "Min gamle ressurs",
                null,
                user.Email!
            ),

            // En annen brukers ressurs med
            // samme e-post, men annen ID.
            CreateResource(
                "Annen brukers aggregat",
                "other-user",
                user.Email!
            ),

            CreateResource(
                "Karis aggregat",
                "user-kari",
                "kari@example.com"
            )
        );

        database.Needs.Add(
            CreateNeed(
                "Offentlig behov",
                "public@example.com",
                DateTime.UtcNow
            )
        );

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user,
            IdentitySeeder.ResourceProviderRole
        );

        var result = await controller.Dashboard();

        var model = GetDashboardModel(result);

        Assert.Equal(2, model.Resources.Count);
        Assert.Equal(2, model.ResourceCount);

        Assert.Contains(
            model.Resources,
            r => r.ResourceName == "Mitt aggregat"
        );

        Assert.Contains(
            model.Resources,
            r => r.ResourceName == "Min gamle ressurs"
        );

        Assert.DoesNotContain(
            model.Resources,
            r => r.ResourceName == "Annen brukers aggregat"
        );

        Assert.DoesNotContain(
            model.Resources,
            r => r.ResourceName == "Karis aggregat"
        );

        Assert.Empty(model.Needs);

        Assert.Equal(
            IdentitySeeder.ResourceProviderRole,
            (string)controller.ViewBag.UserRole
        );

        Assert.Equal(
            "Ola Nordmann",
            (string)controller.ViewBag.UserName
        );
    }


    // ========================================
    // TEST 4
    // E-POSTENDRING PÅVIRKER IKKE ID-BASERT
    // EIERSKAP
    // ========================================

    [Fact]
    public async Task Dashboard_EmailChange_ShouldKeepOwnResources()
    {
        using var database = CreateDatabase();

        var user = CreateUser(
            email: "ny@example.com"
        );

        database.Resources.AddRange(
            CreateResource(
                "Mitt aggregat",
                user.Id,
                "gammel@example.com"
            ),

            CreateResource(
                "Gammel ressurs uten bruker-ID",
                null,
                "gammel@example.com"
            )
        );

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user,
            IdentitySeeder.ResourceProviderRole
        );

        var result = await controller.Dashboard();

        var model = GetDashboardModel(result);

        Assert.Single(model.Resources);

        Assert.Equal(
            "Mitt aggregat",
            model.Resources[0].ResourceName
        );
    }


    // ========================================
    // TEST 5
    // STATISTIKK FOR RESSURSLEVERANDØR
    // ========================================

    [Fact]
    public async Task Dashboard_ShouldCalculateResourceStatistics()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        database.Resources.AddRange(
            CreateResource(
                "Aggregat 1",
                user.Id,
                user.Email!,
                "Tilgjengelig"
            ),

            CreateResource(
                "Aggregat 2",
                user.Id,
                user.Email!,
                "Ikke tilgjengelig"
            ),

            CreateResource(
                "Aggregat 3",
                user.Id,
                user.Email!,
                null
            ),

            CreateResource(
                "Aggregat 4",
                user.Id,
                user.Email!,
                "Ring først"
            ),

            CreateResource(
                "Karis aggregat",
                "other-user",
                "kari@example.com",
                "Tilgjengelig"
            )
        );

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user,
            IdentitySeeder.ResourceProviderRole
        );

        var result = await controller.Dashboard();

        var model = GetDashboardModel(result);

        Assert.Equal(4, model.ResourceCount);
        Assert.Equal(1, model.AvailableCount);
        Assert.Equal(1, model.UnavailableCount);
        Assert.Equal(2, model.UnspecifiedCount);
    }


    // ========================================
    // TEST 6 OG 7
    // OFFENTLIG AKTØR OG ADMIN SER HELE
    // SITUASJONSBILDET
    // ========================================

    [Theory]
    [InlineData("PublicActor")]
    [InlineData("Admin")]
    public async Task Dashboard_PublicRoles_ShouldSeeAllData(
        string role)
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var olderDate = DateTime.UtcNow.AddDays(-2);
        var newerDate = DateTime.UtcNow;

        database.Resources.AddRange(
            CreateResource(
                "Eldre aggregat",
                "owner-1",
                "owner1@example.com",
                createdAt: olderDate
            ),

            CreateResource(
                "Nyere aggregat",
                "owner-2",
                "owner2@example.com",
                createdAt: newerDate
            )
        );

        database.Needs.AddRange(
            CreateNeed(
                "Eldre behov",
                "actor1@example.com",
                olderDate
            ),

            CreateNeed(
                "Nyere behov",
                "actor2@example.com",
                newerDate
            )
        );

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user,
            role
        );

        var result = await controller.Dashboard();

        var model = GetDashboardModel(result);

        Assert.Equal(2, model.Resources.Count);
        Assert.Equal(2, model.Needs.Count);

        Assert.Equal(
            "Nyere aggregat",
            model.Resources[0].ResourceName
        );

        Assert.Equal(
            "Eldre aggregat",
            model.Resources[1].ResourceName
        );

        Assert.Equal(
            "Nyere behov",
            model.Needs[0].Description
        );

        Assert.Equal(
            "Eldre behov",
            model.Needs[1].Description
        );

        Assert.Equal(
            role,
            (string)controller.ViewBag.UserRole
        );
    }


    // ========================================
    // TEST 8
    // ADMIN HAR HØYEST ROLLEPRIORITET
    // ========================================

    [Fact]
    public async Task Dashboard_AdminRole_ShouldHaveHighestPriority()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var controller = CreateController(
            database,
            user,
            IdentitySeeder.ResourceProviderRole,
            IdentitySeeder.PublicActorRole,
            IdentitySeeder.AdminRole
        );

        var result = await controller.Dashboard();

        GetDashboardModel(result);

        Assert.Equal(
            IdentitySeeder.AdminRole,
            (string)controller.ViewBag.UserRole
        );
    }


    // ========================================
    // TEST 9
    // PUBLICACTOR PRIORITERES FREMFOR
    // RESOURCEPROVIDER
    // ========================================

    [Fact]
    public async Task Dashboard_PublicActorRole_ShouldTakePriority()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var controller = CreateController(
            database,
            user,
            IdentitySeeder.ResourceProviderRole,
            IdentitySeeder.PublicActorRole
        );

        var result = await controller.Dashboard();

        GetDashboardModel(result);

        Assert.Equal(
            IdentitySeeder.PublicActorRole,
            (string)controller.ViewBag.UserRole
        );
    }


    // ========================================
    // TEST 10
    // MANGLENDE NAVN BRUKER E-POST I STEDET
    // ========================================

    [Fact]
    public async Task Dashboard_WithoutName_ShouldDisplayEmail()
    {
        using var database = CreateDatabase();

        var user = CreateUser(
            name: " "
        );

        var controller = CreateController(
            database,
            user,
            IdentitySeeder.ResourceProviderRole
        );

        var result = await controller.Dashboard();

        GetDashboardModel(result);

        Assert.Equal(
            user.Email,
            (string)controller.ViewBag.UserName
        );
    }


    // ========================================
    // TEST 11
    // DASHBOARD FUNGERER UTEN RESSURSER
    // ========================================

    [Fact]
    public async Task Dashboard_NoResources_ShouldReturnEmptyOverview()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var controller = CreateController(
            database,
            user,
            IdentitySeeder.ResourceProviderRole
        );

        var result = await controller.Dashboard();

        var model = GetDashboardModel(result);

        Assert.Empty(model.Resources);
        Assert.Empty(model.Needs);

        Assert.Equal(0, model.ResourceCount);
        Assert.Equal(0, model.AvailableCount);
        Assert.Equal(0, model.UnavailableCount);
    }


    // ========================================
    // TEST 12
    // OVERSIKTEN HENTER UT DATA UTEN TRACKING
    // ========================================

    [Fact]
    public async Task Dashboard_ShouldNotTrackReadResources()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        database.Resources.Add(
            CreateResource(
                "Testaggregat",
                user.Id,
                user.Email!
            )
        );

        await database.SaveChangesAsync();

        // Fjern tracking fra opprettelsen.
        database.ChangeTracker.Clear();

        var controller = CreateController(
            database,
            user,
            IdentitySeeder.ResourceProviderRole
        );

        var result = await controller.Dashboard();

        var model = GetDashboardModel(result);

        Assert.Single(model.Resources);

        Assert.Empty(
            database.ChangeTracker.Entries<Resource>()
        );
    }


    // ========================================
    // TEST 13
    // FEILSIDE BRUKER REQUEST-ID
    // ========================================

    [Fact]
    public void Error_ShouldReturnRequestId()
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            null
        );

        controller.HttpContext.TraceIdentifier =
            "test-request-123";

        var result = controller.Error();

        var view = Assert.IsType<ViewResult>(result);

        var model = Assert.IsType<ErrorViewModel>(
            view.Model
        );

        Assert.Equal(
            "test-request-123",
            model.RequestId
        );
    }


    // ========================================
    // TEST 14
    // DASHBOARD KREVER AUTORISASJON
    // ========================================

    [Fact]
    public void Dashboard_ShouldRequireAuthorization()
    {
        var method = typeof(HomeController).GetMethod(
            nameof(HomeController.Dashboard),
            Type.EmptyTypes
        );

        Assert.NotNull(method);

        Assert.NotNull(
            method.GetCustomAttribute<AuthorizeAttribute>()
        );

        Assert.NotNull(
            method.GetCustomAttribute<HttpGetAttribute>()
        );
    }
}
