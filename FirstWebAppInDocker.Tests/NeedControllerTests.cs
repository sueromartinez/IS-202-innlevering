
using System.Reflection;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    // HJELPER - TESTSESSION
    // ========================================

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();

        public bool IsAvailable => true;

        public string Id => "test-session";

        public IEnumerable<string> Keys => _values.Keys;

        public void Clear()
        {
            _values.Clear();
        }

        public Task CommitAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            _values.Remove(key);
        }

        public void Set(string key, byte[] value)
        {
            _values[key] = value;
        }

        public bool TryGetValue(
            string key,
            out byte[] value)
        {
            return _values.TryGetValue(key, out value!);
        }
    }


    // ========================================
    // HJELPER - CONTROLLER
    // ========================================

    private static NeedController CreateController(
        ApplicationDbContext database,
        string? role = "PublicActor",
        string email = "actor@example.com")
    {
        var httpContext = new DefaultHttpContext
        {
            Session = new TestSession()
        };

        if (role != null)
        {
            httpContext.Session.SetString("UserRole", role);
        }

        httpContext.Session.SetString("UserEmail", email);

        return new NeedController(database)
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
    //
    // Verdiene settes direkte ved opprettelse,
    // fordi NeedItemViewModel bruker init.
    // ========================================

    private static NeedItemViewModel CreateNeedItem(
        string description = "Reservekraft ved strømbrudd",
        int id = 0,
        int quantity = 2,
        double? latitude = 58.1467,
        double? longitude = 7.9956)
    {
        return new NeedItemViewModel
        {
            Id = id,
            ResourceType = ResourceType.Generator,
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
    //
    // CreatedAt settes ved opprettelse
    // fordi egenskapen bruker init.
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
    // SKRIVEOPERASJONER KREVER POST OG
    // ANTIFORGERY
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
    // RESSURSLEVERANDØR KAN IKKE REGISTRERE
    // BEHOV
    // ========================================

    [Fact]
    public async Task RegisterNeed_ResourceProvider_ShouldNotSave()
    {
        using var database = CreateDatabase();

        var controller = CreateController(
            database,
            role: "ResourceProvider"
        );

        var result = await controller.RegisterNeed(
            CreateRegistration()
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal(
            "Dashboard",
            redirect.ActionName
        );

        Assert.Equal(
            "Home",
            redirect.ControllerName
        );

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
}
