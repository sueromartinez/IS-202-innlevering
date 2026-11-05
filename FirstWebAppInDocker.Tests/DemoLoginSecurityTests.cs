
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class DemoLoginSecurityTests
{
    // ========================================
    // HJELPER - TESTSESSION
    // ========================================

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();

        public bool IsAvailable => true;

        public string Id => "demo-test-session";

        public IEnumerable<string> Keys => _values.Keys;

        public void Clear() => _values.Clear();

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
    // HJELPER - TESTMILJØ
    // ========================================

    private sealed class DemoTestEnvironment
    {
        public Mock<UserManager<ApplicationUser>> Users { get; }

        public Mock<SignInManager<ApplicationUser>> SignIn { get; }

        public AccountController Controller { get; }

        public DefaultHttpContext HttpContext { get; }

        public DemoTestEnvironment(string environmentName)
        {
            var store =
                new Mock<IUserStore<ApplicationUser>>();

            Users = new Mock<UserManager<ApplicationUser>>(
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

            HttpContext = new DefaultHttpContext
            {
                Session = new TestSession()
            };

            SignIn = new Mock<SignInManager<ApplicationUser>>(
                Users.Object,
                new HttpContextAccessor
                {
                    HttpContext = HttpContext
                },
                Mock.Of<
                    IUserClaimsPrincipalFactory<ApplicationUser>
                >(),
                Options.Create(new IdentityOptions()),
                NullLogger<
                    SignInManager<ApplicationUser>
                >.Instance,
                Mock.Of<IAuthenticationSchemeProvider>(),
                Mock.Of<IUserConfirmation<ApplicationUser>>()
            );

            // Simuler ønsket ASP.NET Core-miljø.
            var hostingEnvironment =
                new Mock<IWebHostEnvironment>();

            hostingEnvironment
                .SetupGet(env => env.EnvironmentName)
                .Returns(environmentName);

            Controller = new AccountController(
                Users.Object,
                SignIn.Object,
                hostingEnvironment.Object
            )
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = HttpContext
                },

                TempData =
                    new Mock<ITempDataDictionary>().Object
            };
        }
    }


    // ========================================
    // HJELPER - OFFENTLIG DEMOBRUKER
    // ========================================

    private static ApplicationUser CreateDemoUser()
    {
        return new ApplicationUser
        {
            Id = "demo-public-actor",
            Name = "Offentlig testbruker",
            UserName = IdentitySeeder.PublicActorEmail,
            Email = IdentitySeeder.PublicActorEmail,
            CreatedAt = DateTime.UtcNow
        };
    }


    // ========================================
    // TEST 1
    // DEVELOPMENT TILLATER DEMOINNLOGGING
    // ========================================

    [Fact]
    public async Task DemoLogin_Development_ShouldSucceed()
    {
        var env = new DemoTestEnvironment("Development");

        var publicActor = CreateDemoUser();

        env.SignIn
            .Setup(manager => manager.SignOutAsync())
            .Returns(Task.CompletedTask);

        env.Users
            .Setup(manager =>
                manager.FindByEmailAsync(
                    IdentitySeeder.PublicActorEmail
                )
            )
            .ReturnsAsync(publicActor);

        env.Users
            .Setup(manager =>
                manager.IsInRoleAsync(
                    publicActor,
                    IdentitySeeder.PublicActorRole
                )
            )
            .ReturnsAsync(true);

        env.SignIn
            .Setup(manager =>
                manager.SignInAsync(
                    publicActor,
                    false,
                    null
                )
            )
            .Returns(Task.CompletedTask);

        var result =
            await env.Controller.DemoPublicActorLogin();

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Dashboard", redirect.ActionName);

        Assert.Equal("Home", redirect.ControllerName);

        Assert.Equal(
            publicActor.Email,
            env.HttpContext.Session.GetString("UserEmail")
        );

        Assert.Equal(
            IdentitySeeder.PublicActorRole,
            env.HttpContext.Session.GetString("UserRole")
        );

        env.SignIn.Verify(
            manager => manager.SignOutAsync(),
            Times.Once
        );

        env.SignIn.Verify(
            manager =>
                manager.SignInAsync(
                    publicActor,
                    false,
                    null
                ),
            Times.Once
        );
    }


    // ========================================
    // TEST 2
    // PRODUCTION BLOKKERER DEMOINNLOGGING
    // ========================================

    [Fact]
    public async Task DemoLogin_Production_ShouldReturnNotFound()
    {
        var env = new DemoTestEnvironment("Production");

        var result =
            await env.Controller.DemoPublicActorLogin();

        Assert.IsType<NotFoundResult>(result);

        // Eksisterende autentisering må ikke
        // påvirkes av et avvist demoforsøk.
        env.SignIn.Verify(
            manager => manager.SignOutAsync(),
            Times.Never
        );

        env.SignIn.Verify(
            manager =>
                manager.SignInAsync(
                    It.IsAny<ApplicationUser>(),
                    It.IsAny<bool>(),
                    It.IsAny<string?>()
                ),
            Times.Never
        );

        // Ikke engang demobrukeren skal hentes.
        env.Users.Verify(
            manager =>
                manager.FindByEmailAsync(
                    It.IsAny<string>()
                ),
            Times.Never
        );
    }


    // ========================================
    // TEST 3
    // BLOKKERT DEMOINNLOGGING SKAL IKKE
    // LOGGE UT EN EKSISTERENDE BRUKER
    // ========================================

    [Fact]
    public async Task DemoLogin_Staging_ShouldPreserveExistingSession()
    {
        var env = new DemoTestEnvironment("Staging");

        // Simuler en bruker som allerede er innlogget.
        env.HttpContext.Session.SetString(
            "UserEmail",
            "existing@example.com"
        );

        env.HttpContext.Session.SetString(
            "UserRole",
            IdentitySeeder.ResourceProviderRole
        );

        var result =
            await env.Controller.DemoPublicActorLogin();

        Assert.IsType<NotFoundResult>(result);

        // Session skal være helt uendret.
        Assert.Equal(
            "existing@example.com",
            env.HttpContext.Session.GetString("UserEmail")
        );

        Assert.Equal(
            IdentitySeeder.ResourceProviderRole,
            env.HttpContext.Session.GetString("UserRole")
        );

        // Den eksisterende Identity-innloggingen
        // skal heller ikke avsluttes.
        env.SignIn.Verify(
            manager => manager.SignOutAsync(),
            Times.Never
        );

        env.SignIn.Verify(
            manager =>
                manager.SignInAsync(
                    It.IsAny<ApplicationUser>(),
                    It.IsAny<bool>(),
                    It.IsAny<string?>()
                ),
            Times.Never
        );
    }
}
