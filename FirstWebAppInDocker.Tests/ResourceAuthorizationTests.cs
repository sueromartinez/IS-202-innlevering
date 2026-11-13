
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
