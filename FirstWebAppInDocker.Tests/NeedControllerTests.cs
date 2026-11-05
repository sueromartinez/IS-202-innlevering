
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

public class NeedControllerTests
{
    // ========================================
    // HJELPER - TESTDATABASE
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
    // HJELPER - CONTROLLER MED IDENTITY
    //
    // Ingen Session brukes.
    // Bruker og roller simuleres med Identity.
    // ========================================

    private static NeedController CreateController(
        ApplicationDbContext database,
        string? role = IdentitySeeder.PublicActorRole,
        string email = "actor@example.com",
        bool isAuthenticated = true,
        bool returnNoUser = false)
    {
        ApplicationUser? currentUser = returnNoUser
            ? null
            : new ApplicationUser
            {
                Id = "test-user-123",
                Name = "Testbruker",
                UserName = email,
                Email = email,
                CreatedAt = DateTime.UtcNow
            };

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

        if (currentUser is not null)
        {
            claims.Add(
                new Claim(
                    ClaimTypes.NameIdentifier,
                    currentUser.Id
                )
            );
        }

        if (role is not null)
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
            isAuthenticated
                ? "TestAuthentication"
                : null
        );

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        return new NeedController(
            database,
            userManager.Object
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            },

            TempData = new Mock<ITempDataDictionary>().Object
        };
    }


    // ========================================
    // HJELPER - GYLDIG BEHOV
    // ========================================

    private static NeedItemViewModel CreateNeedItem(
        string description = "Reservekraft ved strømbrudd",
        int id = 0,
        int quantity = 2,
        double? latitude = 58.1467,
        double? longitude = 7.9956,
        ResourceType? resourceType = ResourceType.Generator)
    {
        return new NeedItemViewModel
        {
            Id = id,
            ResourceType = resourceType,
            Priority = NeedPriority.Acute,
            Quantity = quantity,
            Description = description,
            Latitude = latitude,
            Longitude = longitude
        };
    }


    // ========================================
    // HJELPER - REGISTRERINGSMODELL
    // ========================================

    private static RegisterNeedsViewModel CreateRegistration(
        NeedItemViewModel? item = null)
    {
        var model = new RegisterNeedsViewModel();

        model.Needs.Add(
            item ?? CreateNeedItem()
        );

        return model;
    }


    // ========================================
    // HJELPER - LAGRET BEHOV
    // ========================================

    private static Need CreateStoredNeed(
        string ownerEmail = "actor@example.com",
        string description = "Eksisterende behov",
        DateTime? createdAt = null)
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
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
    }


    // ========================================
    // TEST 1
    // CONTROLLER HAR RIKTIGE ROLLER
    // ========================================

    [Fact]
    public void NeedController_ShouldHaveCorrectAuthorizeAttribute()
    {
        var attribute = typeof(NeedController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);

        var roles = attribute.Roles!
            .Split(
                ',',
                StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries
            );

        Assert.Contains(
            IdentitySeeder.PublicActorRole,
            roles
        );

        Assert.Contains(
            IdentitySeeder.AdminRole,
            roles
        );

        Assert.DoesNotContain(
            IdentitySeeder.ResourceProviderRole,
            roles
        );
    }


    // ========================================
    // TEST 2
    // SKRIVEOPERASJONER KREVER POST
    // OG ANTIFORGERY
    // ========================================

    [Fact]
    public void WriteActions_ShouldRequirePostAndAntiforgery()
    {
        var actions = new[]
        {
            (
                nameof(NeedController.RegisterNeed),
                typeof(RegisterNeedsViewModel)
            ),
            (
                nameof(NeedController.EditNeed),
                typeof(NeedItemViewModel)
            ),
            (
                nameof(NeedController.DeleteNeed),
                typeof(int)
            )
        };

        foreach (var (name, parameterType) in actions)
        {
            var method = typeof(NeedController)
                .GetMethod(
                    name,
                    new[] { parameterType }
                );

            Assert.NotNull(method);

            Assert.NotNull(
                method.GetCustomAttribute<HttpPostAttribute>()
            );

            Assert.NotNull(
                method.GetCustomAttribute<
                    ValidateAntiForgeryTokenAttribute
                >()
            );
        }
    }


    // ========================================
    // TEST 3
    // OFFENTLIG AKTØR KAN REGISTRERE
    // FLERE BEHOV SAMTIDIG
    // ========================================

    [Fact]
    public async Task RegisterNeed_ShouldSaveMultipleNeeds()
    {
        using var database = CreateDatabase();

        var controller = CreateController(database);

        var model = CreateRegistration(
            CreateNeedItem(
                description: " Reservekraft ved strømbrudd "
            )
        );

        model.Needs.Add(
            CreateNeedItem(
                description: "Behov for ekstra aggregat"
            )
        );

        var result = await controller.RegisterNeed(model);

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal(
            "NeedOverview",
            redirect.ActionName
        );

        var needs = await database.Needs.ToListAsync();

        Assert.Equal(2, needs.Count);

        // Eieren skal hentes fra Identity.
        Assert.All(
            needs,
            need => Assert.Equal(
                "actor@example.com",
                need.OwnerEmail
            )
        );

        Assert.Contains(
            needs,
            need => need.Description ==
                "Reservekraft ved strømbrudd"
        );

        Assert.Contains(
            needs,
            need => need.Description ==
                "Behov for ekstra aggregat"
        );

        Assert.All(
            needs,
            need => Assert.Equal(
                ResourceType.Generator,
                need.ResourceType
            )
        );
    }


    // ========================================
    // TEST 4
    // RESSURSLEVERANDØR KAN IKKE
    // REGISTRERE BEHOV
    // ========================================

    [Fact]
    public async Task RegisterNeed_ResourceProvider_ShouldNotSave()
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            role: IdentitySeeder.ResourceProviderRole
        );

        var result = await controller.RegisterNeed(
            CreateRegistration()
        );

        Assert.IsType<ForbidResult>(result);

        Assert.Empty(
            await database.Needs.ToListAsync()
        );
    }


    // ========================================
    // TEST 5
    // MINST ETT BEHOV MÅ REGISTRERES
    // ========================================

    [Fact]
    public async Task RegisterNeed_EmptyList_ShouldFail()
    {
        using var database = CreateDatabase();

        var controller = CreateController(database);

        var model = new RegisterNeedsViewModel();

        model.Needs.Clear();

        var result = await controller.RegisterNeed(model);

        Assert.IsType<ViewResult>(result);

        Assert.False(controller.ModelState.IsValid);

        Assert.Single(model.Needs);

        Assert.Empty(
            await database.Needs.ToListAsync()
        );
    }


    // ========================================
    // TEST 6
    // KARTPLASSERING ER PÅKREVD
    // ========================================

    [Fact]
    public async Task RegisterNeed_WithoutLocation_ShouldFail()
    {
        using var database = CreateDatabase();

        var controller = CreateController(database);

        var model = CreateRegistration(
            CreateNeedItem(
                latitude: null,
                longitude: null
            )
        );

        var result = await controller.RegisterNeed(model);

        Assert.IsType<ViewResult>(result);

        Assert.False(controller.ModelState.IsValid);

        Assert.True(
            controller.ModelState.ContainsKey(
                "Needs[0].Latitude"
            )
        );

        Assert.Empty(
            await database.Needs.ToListAsync()
        );
    }


    // ========================================
    // TEST 7
    // UGYLDIG MODELSTATE SKAL IKKE LAGRES
    // ========================================

    [Fact]
    public async Task RegisterNeed_InvalidModelState_ShouldNotSave()
    {
        using var database = CreateDatabase();

        var controller = CreateController(database);

        controller.ModelState.AddModelError(
            "Needs[0].Description",
            "Beskrivelse er påkrevd."
        );

        var model = CreateRegistration();

        var result = await controller.RegisterNeed(model);

        var view = Assert.IsType<ViewResult>(result);

        Assert.Same(model, view.Model);

        Assert.Empty(
            await database.Needs.ToListAsync()
        );
    }


    // ========================================
    // TEST 8
    // OVERSIKTEN VISER NYESTE BEHOV FØRST
    // ========================================

    [Fact]
    public async Task NeedOverview_ShouldShowNewestFirst()
    {
        using var database = CreateDatabase();

        var older = CreateStoredNeed(
            description: "Eldre behov",
            createdAt: DateTime.UtcNow.AddDays(-2)
        );

        var newer = CreateStoredNeed(
            description: "Nyere behov",
            createdAt: DateTime.UtcNow
        );

        database.Needs.AddRange(older, newer);

        await database.SaveChangesAsync();

        var controller = CreateController(database);

        var result = await controller.NeedOverview();

        var view = Assert.IsType<ViewResult>(result);

        var needs =
            Assert.IsAssignableFrom<IEnumerable<Need>>(
                view.Model
            ).ToList();

        Assert.Equal(2, needs.Count);

        Assert.Equal(
            "Nyere behov",
            needs[0].Description
        );

        Assert.Equal(
            "Eldre behov",
            needs[1].Description
        );
    }


    // ========================================
    // TEST 9
    // BRUKER KAN ENDRE EGET BEHOV
    // ========================================

    [Fact]
    public async Task EditNeed_OwnNeed_ShouldUpdate()
    {
        using var database = CreateDatabase();

        var need = CreateStoredNeed();

        database.Needs.Add(need);

        await database.SaveChangesAsync();

        var controller = CreateController(database);

        var model = CreateNeedItem(
            description: " Oppdatert beskrivelse ",
            id: need.Id,
            quantity: 5
        );

        var result = await controller.EditNeed(model);

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal(
            "NeedOverview",
            redirect.ActionName
        );

        var updated = await database.Needs
            .SingleAsync(n => n.Id == need.Id);

        Assert.Equal(
            "Oppdatert beskrivelse",
            updated.Description
        );

        Assert.Equal(
            5,
            updated.Quantity
        );

        Assert.Equal(
            "actor@example.com",
            updated.OwnerEmail
        );
    }


    // ========================================
    // TEST 10
    // BRUKER KAN IKKE ENDRE ANDRES BEHOV
    // ========================================

    [Fact]
    public async Task EditNeed_OtherUsersNeed_ShouldReturnNotFound()
    {
        using var database = CreateDatabase();

        var need = CreateStoredNeed(
            ownerEmail: "other@example.com"
        );

        database.Needs.Add(need);

        await database.SaveChangesAsync();

        var controller = CreateController(database);

        var model = CreateNeedItem(
            description: "Forsøk på endring",
            id: need.Id
        );

        var result = await controller.EditNeed(model);

        Assert.IsType<NotFoundResult>(result);

        var unchanged = await database.Needs
            .SingleAsync(n => n.Id == need.Id);

        Assert.Equal(
            "Eksisterende behov",
            unchanged.Description
        );
    }


    // ========================================
    // TEST 11
    // BRUKER KAN SLETTE EGET BEHOV
    // ========================================

    [Fact]
    public async Task DeleteNeed_OwnNeed_ShouldDelete()
    {
        using var database = CreateDatabase();

        var need = CreateStoredNeed();

        database.Needs.Add(need);

        await database.SaveChangesAsync();

        var controller = CreateController(database);

        var result = await controller.DeleteNeed(need.Id);

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal(
            "NeedOverview",
            redirect.ActionName
        );

        Assert.Empty(
            await database.Needs.ToListAsync()
        );
    }


    // ========================================
    // TEST 12
    // BRUKER KAN IKKE SLETTE ANDRES BEHOV
    // ========================================

    [Fact]
    public async Task DeleteNeed_OtherUsersNeed_ShouldReturnNotFound()
    {
        using var database = CreateDatabase();

        var need = CreateStoredNeed(
            ownerEmail: "other@example.com"
        );

        database.Needs.Add(need);

        await database.SaveChangesAsync();

        var controller = CreateController(database);

        var result = await controller.DeleteNeed(need.Id);

        Assert.IsType<NotFoundResult>(result);

        Assert.Single(
            await database.Needs.ToListAsync()
        );
    }


    // ========================================
    // TEST 13 OG 14
    // PUBLICACTOR OG ADMIN KAN REGISTRERE
    // ========================================

    [Theory]
    [InlineData("PublicActor")]
    [InlineData("Admin")]
    public async Task RegisterNeed_AllowedRoles_ShouldSave(
        string role)
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            role: role
        );

        var result = await controller.RegisterNeed(
            CreateRegistration()
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal(
            "NeedOverview",
            redirect.ActionName
        );

        var savedNeed = await database.Needs.SingleAsync();

        Assert.Equal(
            "actor@example.com",
            savedNeed.OwnerEmail
        );
    }


    // ========================================
    // TEST 15
    // UINNLOGGET BRUKER SKAL AVVISES
    // ========================================

    [Fact]
    public async Task RegisterNeed_Unauthenticated_ShouldChallenge()
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            isAuthenticated: false
        );

        var result = await controller.RegisterNeed(
            CreateRegistration()
        );

        Assert.IsType<ChallengeResult>(result);

        Assert.Empty(
            await database.Needs.ToListAsync()
        );
    }


    // ========================================
    // TEST 16
    // MANGLENDE IDENTITY-BRUKER SKAL AVVISES
    // ========================================

    [Fact]
    public async Task RegisterNeed_WithoutIdentityUser_ShouldChallenge()
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            returnNoUser: true
        );

        var result = await controller.RegisterNeed(
            CreateRegistration()
        );

        Assert.IsType<ChallengeResult>(result);

        Assert.Empty(
            await database.Needs.ToListAsync()
        );
    }


    // ========================================
    // TEST 17
    // MANGLENDE RESSURSTYPE SKAL AVVISES
    // ========================================

    [Fact]
    public async Task RegisterNeed_MissingResourceType_ShouldFail()
    {
        using var database = CreateDatabase();

        var controller = CreateController(database);

        var model = CreateRegistration(
            CreateNeedItem(
                resourceType: null
            )
        );

        var result = await controller.RegisterNeed(model);

        Assert.IsType<ViewResult>(result);

        Assert.False(controller.ModelState.IsValid);

        Assert.True(
            controller.ModelState.ContainsKey(
                "Needs[0].ResourceType"
            )
        );

        Assert.Empty(
            await database.Needs.ToListAsync()
        );
    }


    // ========================================
    // TEST 18
    // RESSURSLEVERANDØR KAN IKKE ENDRE BEHOV
    // ========================================

    [Fact]
    public async Task EditNeed_ResourceProvider_ShouldForbid()
    {
        using var database = CreateDatabase();

        var need = CreateStoredNeed();

        database.Needs.Add(need);

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            role: IdentitySeeder.ResourceProviderRole
        );

        var model = CreateNeedItem(
            description: "Uautorisert endring",
            id: need.Id
        );

        var result = await controller.EditNeed(model);

        Assert.IsType<ForbidResult>(result);

        var unchanged = await database.Needs
            .SingleAsync(n => n.Id == need.Id);

        Assert.Equal(
            "Eksisterende behov",
            unchanged.Description
        );
    }


    // ========================================
    // TEST 19
    // MANGLENDE IDENTITY-BRUKER KAN IKKE
    // SLETTE BEHOV
    // ========================================

    [Fact]
    public async Task DeleteNeed_WithoutIdentityUser_ShouldChallenge()
    {
        using var database = CreateDatabase();

        var need = CreateStoredNeed();

        database.Needs.Add(need);

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            returnNoUser: true
        );

        var result = await controller.DeleteNeed(need.Id);

        Assert.IsType<ChallengeResult>(result);

        Assert.Single(
            await database.Needs.ToListAsync()
        );
    }
}
