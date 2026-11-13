
using System.Reflection;
using System.Security.Claims;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class ResourceAuthorizationTests
{
    // Kontrollerer lagring av adressefeltene, perioden og de opprinnelige koordinatene samlet.
    [Fact]
    public async Task RegisterResource_SavesAddressAndPeriod_WithOriginalCoordinates()
    {
        using var database = CreateDatabase();
        var controller = CreateController(database, CreateUser());
        var model = CreateResource();
        model.StreetAddress = "Stålveien 9";
        model.PostalLocation = "4629, Kristiansand S";
        model.Municipality = "Kristiansand";
        Assert.IsType<RedirectToActionResult>(await controller.RegisterResource(model));
        var saved = Assert.Single(await database.Resources.ToListAsync());
        Assert.Equal("Stålveien 9, 4629, Kristiansand S – Kristiansand kommune", saved.Address);
        Assert.Equal(model.Latitude, saved.Latitude);
        Assert.Equal(model.Longitude, saved.Longitude);
        Assert.Equal(DateOnly.FromDateTime(saved.CreatedAt.AddMonths(3)),
            FirstWebAppInDocker.Services.ResourceAvailability.GetEndDate(saved.Availability));
        var overview = Assert.IsType<ViewResult>(await controller.ResourceOverview());
        var visible = Assert.Single(Assert.IsType<List<ResourceOverviewViewModel>>(overview.Model));
        Assert.Equal(saved.ResourceName, visible.ResourceName);
        Assert.Equal(saved.Address, visible.Address);
        Assert.Equal(saved.Availability, visible.Availability);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(5)]
    public async Task RegisterResource_RejectsMissingOrUnsupportedPeriod(int? months)
    {
        using var database = CreateDatabase();
        var controller = CreateController(database, CreateUser());
        var model = CreateResource();
        model.AvailabilityMonths = months;
        Assert.IsType<ViewResult>(await controller.RegisterResource(model));
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await database.Resources.ToListAsync());
    }

    [Theory]
    [InlineData(ResourceStatus.Available, "3 måneder; til 2020-01-01", ResourceStatus.Unavailable)]
    [InlineData(ResourceStatus.Busy, "3 måneder; til 2099-01-01", ResourceStatus.Busy)]
    [InlineData(ResourceStatus.Available, "Tilgjengelig", ResourceStatus.Available)]
    public void Availability_UsesStoredStatusAndExpiry(ResourceStatus status, string period, ResourceStatus expected)
    {
        Assert.Equal(expected, FirstWebAppInDocker.Services.ResourceAvailability.EffectiveStatus(
            status, period, new DateTime(2026, 11, 13)));
    }

    [Fact]
    public void Dashboard_CountsNewPeriodsAlongsideLegacyAvailability()
    {
        var model = new DashboardViewModel { Resources = new()
        {
            new() { Availability = "3 måneder; til 2099-01-01", Status = ResourceStatus.Available },
            new() { Availability = "3 måneder; til 2020-01-01", Status = ResourceStatus.Available },
            new() { Availability = "Tilgjengelig" }
        }};
        Assert.Equal(2, model.AvailableCount);
        Assert.Equal(1, model.UnavailableCount);
        Assert.Equal(0, model.UnspecifiedCount);
    }

    // ========================================
    // HJELPER - TESTDATABASE
    // ========================================

    private static ApplicationDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString()
                )
                .Options;

        return new ApplicationDbContext(options);
    }


    // ========================================
    // HJELPER - TESTBRUKER
    // ========================================

    private static ApplicationUser CreateUser()
    {
        return new ApplicationUser
        {
            Id = "resource-provider-123",
            Name = "Ola Nordmann",
            UserName = "ola@example.com",
            Email = "ola@example.com",
            CreatedAt = DateTime.UtcNow
        };
    }


    // ========================================
    // HJELPER - GYLDIG RESSURS
    // ========================================

    private static RegisterResourceViewModel CreateResource()
    {
        return new RegisterResourceViewModel
        {
            ResourceName = "Aggregat",
            ResourceType = ResourceType.Generator,
            Description = "Reservekraft ved strømbrudd",
            Quantity = 2,
            Capacity = "25 kVA",
            Availability = "Tilgjengelig",
            AvailabilityMonths = 3,
            Address = "Kristiansand",
            Latitude = 58.1467,
            Longitude = 7.9956,
            ContactEmail = "kontakt@example.com",
            ContactPhoneCountryCode = "+47",
            ContactPhoneNumber = "12345678"
        };
    }


    // ========================================
    // HJELPER - CONTROLLER MED MOCK IDENTITY
    // ========================================

    private static ResourceController CreateController(
        ApplicationDbContext database,
        ApplicationUser? user
    )
    {
        var userStore =
            new Mock<IUserStore<ApplicationUser>>();

        var userManager =
            new Mock<UserManager<ApplicationUser>>(
                userStore.Object,
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
            .ReturnsAsync(user);


        var claims = new List<Claim>
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                user?.Id ?? "test-user"
            ),

            new Claim(
                ClaimTypes.Role,
                IdentitySeeder.ResourceProviderRole
            )
        };

        var identity = new ClaimsIdentity(
            claims,
            "TestAuthentication"
        );

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        var tempData =
            new Mock<ITempDataDictionary>();

        return new ResourceController(
            database,
            userManager.Object
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            },

            TempData = tempData.Object
        };
    }


    // ========================================
    // TEST 1
    // KONTROLLEREN KREVER RIKTIGE ROLLER
    // ========================================

    [Fact]
    public void ResourceController_ShouldRequireAuthorizedRole()
    {
        var attribute =
            typeof(ResourceController)
                .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);

        var roles = attribute.Roles!
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries
            );

        Assert.Contains(
            IdentitySeeder.ResourceProviderRole,
            roles
        );

        Assert.Contains(
            IdentitySeeder.PublicActorRole,
            roles
        );

        Assert.Contains(
            IdentitySeeder.AdminRole,
            roles
        );
    }


    // ========================================
    // TEST 2
    // BARE RESSURSLEVERANDØR KAN REGISTRERE
    // ========================================

    [Fact]
    public void RegisterResource_ShouldRequireResourceProviderRole()
    {
        var method = typeof(ResourceController).GetMethod(
            nameof(ResourceController.RegisterResource),
            new[] { typeof(RegisterResourceViewModel) }
        );

        Assert.NotNull(method);

        var authorize =
            method.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);

        Assert.Equal(
            IdentitySeeder.ResourceProviderRole,
            authorize.Roles
        );

        Assert.NotNull(
            method.GetCustomAttribute<HttpPostAttribute>()
        );
    }


    // ========================================
    // TEST 3
    // REGISTRERING KREVER ANTIFORGERY
    // ========================================

    [Fact]
    public void RegisterResource_ShouldRequireAntiForgeryToken()
    {
        var method = typeof(ResourceController).GetMethod(
            nameof(ResourceController.RegisterResource),
            new[] { typeof(RegisterResourceViewModel) }
        );

        Assert.NotNull(method);

        var attribute =
            method.GetCustomAttribute<
                ValidateAntiForgeryTokenAttribute
            >();

        Assert.NotNull(attribute);
    }


    // ========================================
    // TEST 4
    // RESSURS REGISTRERES PÅ RIKTIG BRUKER
    // ========================================

    [Fact]
    public async Task RegisterResource_ShouldSaveCorrectOwnerUserId()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var controller = CreateController(
            database,
            user
        );

        var model = CreateResource();

        var result = await controller.RegisterResource(model);

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        // Etter registrering sendes brukeren
        // til Dashboard, hvor registreringen
        // bekreftes med feiringsanimasjonen.

        Assert.Equal(
            "Dashboard",
            redirect.ActionName
        );

        Assert.Equal(
            "Home",
            redirect.ControllerName
        );

        var resource =
            await database.Resources.SingleAsync();

        Assert.Equal(
            user.Id,
            resource.OwnerUserId
        );

        Assert.Equal(
            user.Email,
            resource.OwnerEmail
        );

        Assert.Equal(
            "Aggregat",
            resource.ResourceName
        );

        Assert.Equal(
            2,
            resource.Quantity
        );
    }


    // ========================================
    // TEST 5
    // UINNLOGGET ELLER UKJENT BRUKER
    // SKAL IKKE FÅ LAGRET RESSURS
    // ========================================

    [Fact]
    public async Task RegisterResource_WithoutIdentityUser_ShouldNotSave()
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            null
        );

        var model = CreateResource();

        var result = await controller.RegisterResource(model);

        Assert.IsType<ChallengeResult>(result);

        Assert.Empty(
            await database.Resources.ToListAsync()
        );
    }


    // ========================================
    // TEST 6
    // UGYLDIG MODELSTATE SKAL IKKE LAGRES
    // ========================================

    [Fact]
    public async Task RegisterResource_InvalidModel_ShouldNotSave()
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            CreateUser()
        );

        var model = CreateResource();

        controller.ModelState.AddModelError(
            "ResourceName",
            "Ressursnavn mangler."
        );

        var result = await controller.RegisterResource(model);

        var view = Assert.IsType<ViewResult>(result);

        Assert.Same(model, view.Model);

        Assert.Empty(
            await database.Resources.ToListAsync()
        );
    }


    // ========================================
    // TEST 7
    // MANGLENDE RESSURSTYPE SKAL IKKE LAGRES
    // ========================================

    [Fact]
    public async Task RegisterResource_WithoutResourceType_ShouldNotSave()
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            CreateUser()
        );

        var model = CreateResource();

        model.ResourceType = null;

        var result = await controller.RegisterResource(model);

        Assert.IsType<ViewResult>(result);

        Assert.False(controller.ModelState.IsValid);

        Assert.True(
            controller.ModelState.ContainsKey("ResourceType")
        );

        Assert.Empty(
            await database.Resources.ToListAsync()
        );
    }
}
