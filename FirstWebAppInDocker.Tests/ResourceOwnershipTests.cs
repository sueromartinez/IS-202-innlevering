using System.Security.Claims;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class ResourceOwnershipTests
{
    // ========================================
    // TESTDATABASE
    // ========================================

    private static ApplicationDbContext CreateContext()
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
    // TESTBRUKER
    // ========================================

    private static ApplicationUser CreateUser(
        string id,
        string email
    )
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = email,
            Email = email,
            Name = "Testbruker",
            CreatedAt = DateTime.UtcNow
        };
    }


    // ========================================
    // TESTRESSURS
    // ========================================

    private static Resource CreateResource(
        string name,
        string ownerUserId,
        string ownerEmail
    )
    {
        return new Resource
        {
            ResourceName = name,
            ResourceType = ResourceType.Generator,
            Description = "Testressurs",
            Quantity = 1,
            Availability = "Tilgjengelig",

            Latitude = 58.1467,
            Longitude = 7.9956,

            OwnerUserId = ownerUserId,
            OwnerEmail = ownerEmail,

            CreatedAt = DateTime.UtcNow
        };
    }


    // ========================================
    // CONTROLLER MED TESTIDENTITY
    // ========================================

    private static ResourceController CreateController(
        ApplicationDbContext context,
        ApplicationUser currentUser
    )
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
            .Setup(
                manager => manager.GetUserAsync(
                    It.IsAny<ClaimsPrincipal>()
                )
            )
            .ReturnsAsync(currentUser);


        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                currentUser.Id
            ),

            new Claim(
                ClaimTypes.Role,
                IdentitySeeder.ResourceProviderRole
            )
        };


        var identity =
            new ClaimsIdentity(
                claims,
                "TestAuthentication"
            );


        var httpContext =
            new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            };


        return new ResourceController(
            context,
            userManager.Object
        )
        {
            ControllerContext =
                new ControllerContext
                {
                    HttpContext = httpContext
                }
        };
    }


    // ========================================
    // TEST 1
    // BRUKER SER BARE EGNE RESSURSER
    // ========================================

    [Fact]
    public async Task ResourceOverview_ShouldOnlyShowOwnResources()
    {
        using var context = CreateContext();

        var ola = CreateUser(
            "user-ola",
            "ola@example.com"
        );

        var kari = CreateUser(
            "user-kari",
            "kari@example.com"
        );


        context.Resources.AddRange(
            CreateResource(
                "Olas aggregat",
                ola.Id,
                ola.Email!
            ),

            CreateResource(
                "Olas drone",
                ola.Id,
                ola.Email!
            ),

            CreateResource(
                "Karis traktor",
                kari.Id,
                kari.Email!
            )
        );


        await context.SaveChangesAsync();


        var controller =
            CreateController(context, ola);


        var result =
            await controller.ResourceOverview();


        var view =
            Assert.IsType<ViewResult>(result);


        var resources =
            Assert.IsType<List<ResourceOverviewViewModel>>(
                view.Model
            );


        Assert.Equal(2, resources.Count);

        Assert.DoesNotContain(
            resources,
            resource =>
                resource.OwnerEmail == kari.Email
        );
    }


    // ========================================
    // TEST 2
    // RESSURSER BEHOLDES VED E-POSTENDRING
    // ========================================

    [Fact]
    public async Task ResourceOverview_ShouldKeepResourcesAfterEmailChange()
    {
        using var context = CreateContext();


        var ola = CreateUser(
            "user-ola",
            "ola1@example.com"
        );


        // Ressursen ble registrert med
        // den gamle e-postadressen.
        //
        // Identity-ID-en er fortsatt lik.

        context.Resources.Add(
            CreateResource(
                "Olas aggregat",
                ola.Id,
                "ola@example.com"
            )
        );


        await context.SaveChangesAsync();


        var controller =
            CreateController(context, ola);


        var result =
            await controller.ResourceOverview();


        var view =
            Assert.IsType<ViewResult>(result);


        var resources =
            Assert.IsType<List<ResourceOverviewViewModel>>(
                view.Model
            );


        Assert.Single(resources);
    }


    // ========================================
    // TEST 3
    // E-POST GIR IKKE EIERSKAP TIL ANDRES RESSURSER
    // ========================================

    [Fact]
    public async Task ResourceOverview_ShouldRejectResourcesOwnedByAnotherUser()
    {
        using var context = CreateContext();


        var ola = CreateUser(
            "user-ola",
            "ola@example.com"
        );


        // Ressursen har samme e-postadresse,
        // men tilhører en annen Identity-ID.
        //
        // E-post skal ikke overstyre
        // et eksisterende OwnerUserId.

        context.Resources.Add(
            CreateResource(
                "Annen brukers ressurs",
                "user-other",
                ola.Email!
            )
        );


        await context.SaveChangesAsync();


        var controller =
            CreateController(context, ola);


        var result =
            await controller.ResourceOverview();


        var view =
            Assert.IsType<ViewResult>(result);


        var resources =
            Assert.IsType<List<ResourceOverviewViewModel>>(
                view.Model
            );


        Assert.Empty(resources);
    }
}