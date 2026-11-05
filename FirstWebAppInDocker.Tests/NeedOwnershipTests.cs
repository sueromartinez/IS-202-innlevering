
using System.Security.Claims;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class NeedOwnershipTests
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
    // HJELPER - IDENTITY-BRUKER
    // ========================================

    private static ApplicationUser CreateUser(
        string id = "actor-123",
        string email = "actor@example.com")
    {
        return new ApplicationUser
        {
            Id = id,
            Name = "Offentlig testbruker",
            UserName = email,
            Email = email,
            CreatedAt = DateTime.UtcNow
        };
    }


    // ========================================
    // HJELPER - NEEDCONTROLLER MED IDENTITY
    //
    // KORRIGERT:
    // UserManager får riktig antall argumenter.
    // ========================================

    private static NeedController CreateController(
        ApplicationDbContext database,
        ApplicationUser user)
    {
        var store =
            new Mock<IUserStore<ApplicationUser>>();

        // UserManager har 9 konstruktørparametere:
        // IUserStore + 8 øvrige parametere.
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
            .ReturnsAsync(user);

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                user.Id
            ),

            new Claim(
                ClaimTypes.Role,
                IdentitySeeder.PublicActorRole
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
    // HJELPER - REGISTRERING AV BEHOV
    // ========================================

    private static RegisterNeedsViewModel CreateRegistration()
    {
        var model = new RegisterNeedsViewModel();

        model.Needs.Add(
            new NeedItemViewModel
            {
                ResourceType = ResourceType.Generator,
                Priority = NeedPriority.Acute,
                Quantity = 2,
                Description = "Behov for reservekraft",
                Latitude = 58.1467,
                Longitude = 7.9956
            }
        );

        return model;
    }


    // ========================================
    // HJELPER - REDIGERING AV BEHOV
    // ========================================

    private static NeedItemViewModel CreateEditModel(int id)
    {
        return new NeedItemViewModel
        {
            Id = id,
            ResourceType = ResourceType.Generator,
            Priority = NeedPriority.Planned,
            Quantity = 5,
            Description = "Oppdatert behov",
            Latitude = 58.15,
            Longitude = 8.0
        };
    }


    // ========================================
    // HJELPER - LAGRET BEHOV
    // ========================================

    private static Need CreateStoredNeed(
        string ownerEmail,
        string? ownerUserId,
        string description = "Eksisterende behov")
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
            OwnerUserId = ownerUserId,
            CreatedAt = DateTime.UtcNow
        };
    }


    // ========================================
    // TEST 1
    // NYE BEHOV FÅR RIKTIG OWNERUSERID
    // ========================================

    [Fact]
    public async Task RegisterNeed_ShouldSaveOwnerUserId()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var controller = CreateController(
            database,
            user
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

        var need = await database.Needs.SingleAsync();

        Assert.Equal(
            user.Id,
            need.OwnerUserId
        );

        Assert.Equal(
            user.Email,
            need.OwnerEmail
        );

        Assert.Equal(
            "Behov for reservekraft",
            need.Description
        );
    }


    // ========================================
    // TEST 2
    // E-POSTENDRING HINDRER IKKE REDIGERING
    // ========================================

    [Fact]
    public async Task EditNeed_AfterEmailChange_ShouldSucceed()
    {
        using var database = CreateDatabase();

        var user = CreateUser(
            id: "actor-123",
            email: "ny@example.com"
        );

        var need = CreateStoredNeed(
            ownerEmail: "gammel@example.com",
            ownerUserId: user.Id
        );

        database.Needs.Add(need);

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user
        );

        var result = await controller.EditNeed(
            CreateEditModel(need.Id)
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal(
            "NeedOverview",
            redirect.ActionName
        );

        var updated = await database.Needs
            .SingleAsync(n => n.Id == need.Id);

        Assert.Equal(
            "Oppdatert behov",
            updated.Description
        );

        Assert.Equal(
            user.Id,
            updated.OwnerUserId
        );

        Assert.Equal(
            "gammel@example.com",
            updated.OwnerEmail
        );
    }


    // ========================================
    // TEST 3
    // E-POSTENDRING HINDRER IKKE SLETTING
    // ========================================

    [Fact]
    public async Task DeleteNeed_AfterEmailChange_ShouldSucceed()
    {
        using var database = CreateDatabase();

        var user = CreateUser(
            id: "actor-123",
            email: "ny@example.com"
        );

        var need = CreateStoredNeed(
            ownerEmail: "gammel@example.com",
            ownerUserId: user.Id
        );

        database.Needs.Add(need);

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user
        );

        var result = await controller.DeleteNeed(
            need.Id
        );

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
    // TEST 4
    // ANDRE KAN IKKE REDIGERE BEHOV
    // SELV OM E-POSTEN ER LIK
    // ========================================

    [Fact]
    public async Task EditNeed_DifferentOwnerId_ShouldReturnNotFound()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var need = CreateStoredNeed(
            ownerEmail: user.Email!,
            ownerUserId: "another-user-456"
        );

        database.Needs.Add(need);

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user
        );

        var result = await controller.EditNeed(
            CreateEditModel(need.Id)
        );

        Assert.IsType<NotFoundResult>(result);

        var unchanged = await database.Needs
            .SingleAsync(n => n.Id == need.Id);

        Assert.Equal(
            "Eksisterende behov",
            unchanged.Description
        );

        Assert.Equal(
            "another-user-456",
            unchanged.OwnerUserId
        );
    }


    // ========================================
    // TEST 5
    // ANDRE KAN IKKE SLETTE BEHOV
    // SELV OM E-POSTEN ER LIK
    // ========================================

    [Fact]
    public async Task DeleteNeed_DifferentOwnerId_ShouldReturnNotFound()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var need = CreateStoredNeed(
            ownerEmail: user.Email!,
            ownerUserId: "another-user-456"
        );

        database.Needs.Add(need);

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user
        );

        var result = await controller.DeleteNeed(
            need.Id
        );

        Assert.IsType<NotFoundResult>(result);

        Assert.Single(
            await database.Needs.ToListAsync()
        );
    }


    // ========================================
    // TEST 6
    // GAMLE BEHOV KOBLES TIL RIKTIG BRUKER
    // ========================================

    [Fact]
    public async Task NeedOverview_ShouldLinkOnlyMatchingLegacyNeeds()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var ownLegacyNeed = CreateStoredNeed(
            ownerEmail: user.Email!,
            ownerUserId: null,
            description: "Mitt gamle behov"
        );

        var otherLegacyNeed = CreateStoredNeed(
            ownerEmail: "other@example.com",
            ownerUserId: null,
            description: "Annen brukers gamle behov"
        );

        var alreadyOwnedNeed = CreateStoredNeed(
            ownerEmail: user.Email!,
            ownerUserId: "another-user-456",
            description: "Allerede eid av en annen"
        );

        database.Needs.AddRange(
            ownLegacyNeed,
            otherLegacyNeed,
            alreadyOwnedNeed
        );

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user
        );

        var result = await controller.NeedOverview();

        Assert.IsType<ViewResult>(result);

        var ownUpdated = await database.Needs
            .SingleAsync(n => n.Id == ownLegacyNeed.Id);

        var otherUpdated = await database.Needs
            .SingleAsync(n => n.Id == otherLegacyNeed.Id);

        var alreadyOwnedUpdated = await database.Needs
            .SingleAsync(n => n.Id == alreadyOwnedNeed.Id);

        Assert.Equal(
            user.Id,
            ownUpdated.OwnerUserId
        );

        Assert.Null(
            otherUpdated.OwnerUserId
        );

        Assert.Equal(
            "another-user-456",
            alreadyOwnedUpdated.OwnerUserId
        );
    }


    // ========================================
    // TEST 7
    // GAMMELT BEHOV FÅR PERMANENT EIERSKAP
    // VED REDIGERING
    // ========================================

    [Fact]
    public async Task EditNeed_LegacyNeed_ShouldAssignOwnerUserId()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var legacyNeed = CreateStoredNeed(
            ownerEmail: user.Email!,
            ownerUserId: null
        );

        database.Needs.Add(legacyNeed);

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user
        );

        var result = await controller.EditNeed(
            CreateEditModel(legacyNeed.Id)
        );

        Assert.IsType<RedirectToActionResult>(result);

        var updated = await database.Needs
            .SingleAsync(n => n.Id == legacyNeed.Id);

        Assert.Equal(
            user.Id,
            updated.OwnerUserId
        );

        Assert.Equal(
            "Oppdatert behov",
            updated.Description
        );
    }


    // ========================================
    // TEST 8
    // GAMLE BEHOV MED ANNEN E-POST AVVISES
    // ========================================

    [Fact]
    public async Task EditNeed_LegacyNeedWithOtherEmail_ShouldReturnNotFound()
    {
        using var database = CreateDatabase();

        var user = CreateUser();

        var legacyNeed = CreateStoredNeed(
            ownerEmail: "other@example.com",
            ownerUserId: null
        );

        database.Needs.Add(legacyNeed);

        await database.SaveChangesAsync();

        var controller = CreateController(
            database,
            user
        );

        var result = await controller.EditNeed(
            CreateEditModel(legacyNeed.Id)
        );

        Assert.IsType<NotFoundResult>(result);

        var unchanged = await database.Needs
            .SingleAsync(n => n.Id == legacyNeed.Id);

        Assert.Null(
            unchanged.OwnerUserId
        );

        Assert.Equal(
            "Eksisterende behov",
            unchanged.Description
        );
    }
}
