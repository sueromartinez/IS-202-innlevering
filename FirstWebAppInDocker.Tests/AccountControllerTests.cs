using System.Reflection;
using System.Security.Claims;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

// Skiller Identity sitt SignInResult fra MVC sitt.
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace FirstWebAppInDocker.Tests;

public class AccountControllerTests
{
    // ========================================
    // HJELPER - TESTSESSION
    // ========================================

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();

        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _values.Keys;

        public void Clear() => _values.Clear();

        public Task CommitAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task LoadAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Remove(string key) => _values.Remove(key);

        public void Set(string key, byte[] value)
            => _values[key] = value;

        public bool TryGetValue(string key, out byte[] value)
            => _values.TryGetValue(key, out value!);
    }

    // ========================================
    // HJELPER - TESTMILJØ MED IDENTITY
    // ========================================

    private sealed class TestEnvironment
    {
        public Mock<UserManager<ApplicationUser>> Users { get; }

        public Mock<SignInManager<ApplicationUser>> SignIn { get; }

        public AccountController Controller { get; }

        public DefaultHttpContext HttpContext { get; }

        public TestEnvironment()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();

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
                Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
                Options.Create(new IdentityOptions()),
                NullLogger<SignInManager<ApplicationUser>>.Instance,
                Mock.Of<IAuthenticationSchemeProvider>(),
                Mock.Of<IUserConfirmation<ApplicationUser>>()
            );

            Controller = new AccountController(
                Users.Object,
                SignIn.Object
            )
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = HttpContext
                },
                TempData = new Mock<ITempDataDictionary>().Object
            };
        }

        public void SetCurrentUser(ApplicationUser user)
        {
            Users.Setup(manager =>
                manager.GetUserAsync(
                    It.IsAny<ClaimsPrincipal>()
                )
            ).ReturnsAsync(user);
        }

        public void AllowProfileUpdate()
        {
            Users.Setup(manager =>
                manager.UpdateAsync(
                    It.IsAny<ApplicationUser>()
                )
            ).ReturnsAsync(IdentityResult.Success);

            SignIn.Setup(manager =>
                manager.RefreshSignInAsync(
                    It.IsAny<ApplicationUser>()
                )
            ).Returns(Task.CompletedTask);
        }
    }

    // ========================================
    // HJELPER - TESTBRUKER
    // ========================================

    private static ApplicationUser CreateUser()
    {
        return new ApplicationUser
        {
            Id = "test-user-123",
            Name = "Ola Nordmann",
            UserName = "ola@example.com",
            Email = "ola@example.com",
            EmailConfirmed = true,
            PhoneNumber = "+4712345678",
            CreatedAt = DateTime.UtcNow
        };
    }

    // ========================================
    // HJELPER - REGISTRERING
    // ========================================

    private static RegisterViewModel CreateRegistration()
    {
        return new RegisterViewModel
        {
            Name = " Ola Nordmann ",
            Email = " ola@example.com ",
            Password = "TestPassord123!"
        };
    }

    // ========================================
    // HJELPER - INNLOGGING
    // ========================================

    private static LoginViewModel CreateLogin()
    {
        return new LoginViewModel
        {
            Email = " ola@example.com ",
            Password = "TestPassord123!"
        };
    }

    // ========================================
    // HJELPER - PROFIL
    // ========================================

    private static ProfileViewModel CreateProfile(
        string name = "Ola Nordmann",
        string email = "ola@example.com",
        string phoneNumber = "12345678")
    {
        return new ProfileViewModel
        {
            Name = name,
            Email = email,
            PhoneCountryCode = "+47",
            PhoneNumber = phoneNumber
        };
    }

    // ========================================
    // TEST 1
    // REGISTRERING GIR RESOURCEPROVIDER
    // ========================================

    [Fact]
    public async Task Register_ShouldAssignResourceProviderRole()
    {
        var env = new TestEnvironment();

        ApplicationUser? createdUser = null;

        env.Users.Setup(manager =>
            manager.FindByEmailAsync("ola@example.com")
        ).ReturnsAsync((ApplicationUser?)null);

        env.Users.Setup(manager =>
            manager.CreateAsync(
                It.IsAny<ApplicationUser>(),
                "TestPassord123!"
            )
        )
        .Callback<ApplicationUser, string>(
            (user, _) => createdUser = user
        )
        .ReturnsAsync(IdentityResult.Success);

        env.Users.Setup(manager =>
            manager.AddToRoleAsync(
                It.IsAny<ApplicationUser>(),
                IdentitySeeder.ResourceProviderRole
            )
        ).ReturnsAsync(IdentityResult.Success);

        env.SignIn.Setup(manager =>
            manager.SignInAsync(
                It.IsAny<ApplicationUser>(),
                false,
                null
            )
        ).Returns(Task.CompletedTask);

        var result = await env.Controller.Register(
            CreateRegistration()
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Dashboard", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);

        Assert.NotNull(createdUser);

        Assert.Equal("Ola Nordmann", createdUser.Name);
        Assert.Equal("ola@example.com", createdUser.Email);
        Assert.Equal("ola@example.com", createdUser.UserName);
        Assert.False(createdUser.EmailConfirmed);

        env.Users.Verify(manager =>
            manager.AddToRoleAsync(
                It.IsAny<ApplicationUser>(),
                IdentitySeeder.ResourceProviderRole
            ),
            Times.Once
        );

        Assert.Equal(
            IdentitySeeder.ResourceProviderRole,
            env.HttpContext.Session.GetString("UserRole")
        );

        Assert.Equal(
            "ola@example.com",
            env.HttpContext.Session.GetString("UserEmail")
        );
    }

    // ========================================
    // TEST 2
    // UGYLDIG REGISTRERING AVVISES
    // ========================================

    [Fact]
    public async Task Register_InvalidModel_ShouldNotCreateUser()
    {
        var env = new TestEnvironment();

        env.Controller.ModelState.AddModelError(
            "Email",
            "Ugyldig e-postadresse."
        );

        var result = await env.Controller.Register(
            CreateRegistration()
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Index", redirect.ActionName);

        env.Users.Verify(manager =>
            manager.CreateAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 3
    // DUPLIKAT E-POST AVVISES
    // ========================================

    [Fact]
    public async Task Register_DuplicateEmail_ShouldNotCreateUser()
    {
        var env = new TestEnvironment();

        env.Users.Setup(manager =>
            manager.FindByEmailAsync("ola@example.com")
        ).ReturnsAsync(CreateUser());

        var result = await env.Controller.Register(
            CreateRegistration()
        );

        Assert.IsType<RedirectToActionResult>(result);

        env.Users.Verify(manager =>
            manager.CreateAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 4
    // FEIL VED BRUKEROPPRETTELSE
    // ========================================

    [Fact]
    public async Task Register_CreateFails_ShouldNotAssignRole()
    {
        var env = new TestEnvironment();

        env.Users.Setup(manager =>
            manager.FindByEmailAsync(
                It.IsAny<string>()
            )
        ).ReturnsAsync((ApplicationUser?)null);

        env.Users.Setup(manager =>
            manager.CreateAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>()
            )
        ).ReturnsAsync(
            IdentityResult.Failed(
                new IdentityError
                {
                    Description = "Brukeren kunne ikke opprettes."
                }
            )
        );

        var result = await env.Controller.Register(
            CreateRegistration()
        );

        Assert.IsType<RedirectToActionResult>(result);

        env.Users.Verify(manager =>
            manager.AddToRoleAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 5
    // MISLYKKET ROLLETILDELING
    // ========================================

    [Fact]
    public async Task Register_RoleFails_ShouldDeleteCreatedUser()
    {
        var env = new TestEnvironment();

        env.Users.Setup(manager =>
            manager.FindByEmailAsync(
                It.IsAny<string>()
            )
        ).ReturnsAsync((ApplicationUser?)null);

        env.Users.Setup(manager =>
            manager.CreateAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>()
            )
        ).ReturnsAsync(IdentityResult.Success);

        env.Users.Setup(manager =>
            manager.AddToRoleAsync(
                It.IsAny<ApplicationUser>(),
                IdentitySeeder.ResourceProviderRole
            )
        ).ReturnsAsync(
            IdentityResult.Failed(
                new IdentityError
                {
                    Description = "Kunne ikke tildele rolle."
                }
            )
        );

        env.Users.Setup(manager =>
            manager.DeleteAsync(
                It.IsAny<ApplicationUser>()
            )
        ).ReturnsAsync(IdentityResult.Success);

        var result = await env.Controller.Register(
            CreateRegistration()
        );

        Assert.IsType<RedirectToActionResult>(result);

        env.Users.Verify(manager =>
            manager.DeleteAsync(
                It.IsAny<ApplicationUser>()
            ),
            Times.Once
        );

        Assert.Null(
            env.HttpContext.Session.GetString("UserEmail")
        );
    }

    // ========================================
    // TEST 6
    // VELLYKKET INNLOGGING
    // ========================================

    [Fact]
    public async Task Login_Success_ShouldCreateSession()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.Users.Setup(manager =>
            manager.FindByEmailAsync("ola@example.com")
        ).ReturnsAsync(user);

        env.SignIn.Setup(manager =>
            manager.PasswordSignInAsync(
                user,
                "TestPassord123!",
                false,
                true
            )
        ).ReturnsAsync(IdentitySignInResult.Success);

        IList<string> roles = new List<string>
        {
            IdentitySeeder.ResourceProviderRole
        };

        env.Users.Setup(manager =>
            manager.GetRolesAsync(user)
        ).ReturnsAsync(roles);

        var result = await env.Controller.Login(
            CreateLogin()
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Dashboard", redirect.ActionName);

        Assert.Equal(
            user.Email,
            env.HttpContext.Session.GetString("UserEmail")
        );

        Assert.Equal(
            user.Name,
            env.HttpContext.Session.GetString("UserName")
        );

        Assert.Equal(
            IdentitySeeder.ResourceProviderRole,
            env.HttpContext.Session.GetString("UserRole")
        );

        env.SignIn.Verify(manager =>
            manager.PasswordSignInAsync(
                user,
                "TestPassord123!",
                false,
                true
            ),
            Times.Once
        );
    }

    // ========================================
    // TEST 7
    // UKJENT E-POST
    // ========================================

    [Fact]
    public async Task Login_UnknownEmail_ShouldNotSignIn()
    {
        var env = new TestEnvironment();

        env.Users.Setup(manager =>
            manager.FindByEmailAsync("ola@example.com")
        ).ReturnsAsync((ApplicationUser?)null);

        var result = await env.Controller.Login(
            CreateLogin()
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Index", redirect.ActionName);

        env.SignIn.Verify(manager =>
            manager.PasswordSignInAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()
            ),
            Times.Never
        );

        Assert.Null(
            env.HttpContext.Session.GetString("UserEmail")
        );
    }

    // ========================================
    // TEST 8
    // FEIL PASSORD
    // ========================================

    [Fact]
    public async Task Login_WrongPassword_ShouldNotCreateSession()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.Users.Setup(manager =>
            manager.FindByEmailAsync("ola@example.com")
        ).ReturnsAsync(user);

        env.SignIn.Setup(manager =>
            manager.PasswordSignInAsync(
                user,
                It.IsAny<string>(),
                false,
                true
            )
        ).ReturnsAsync(IdentitySignInResult.Failed);

        var result = await env.Controller.Login(
            CreateLogin()
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Index", redirect.ActionName);

        Assert.Null(
            env.HttpContext.Session.GetString("UserEmail")
        );
    }

    // ========================================
    // TEST 9
    // TOFAKTOR KREVES
    // ========================================

    [Fact]
    public async Task Login_TwoFactorRequired_ShouldNotCreateSession()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.Users.Setup(manager =>
            manager.FindByEmailAsync("ola@example.com")
        ).ReturnsAsync(user);

        env.SignIn.Setup(manager =>
            manager.PasswordSignInAsync(
                user,
                It.IsAny<string>(),
                false,
                true
            )
        ).ReturnsAsync(IdentitySignInResult.TwoFactorRequired);

        var result = await env.Controller.Login(
            CreateLogin()
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Index", redirect.ActionName);

        Assert.Null(
            env.HttpContext.Session.GetString("UserEmail")
        );
    }

    // ========================================
    // TEST 10
    // LÅST KONTO
    // ========================================

    [Fact]
    public async Task Login_LockedOut_ShouldNotCreateSession()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.Users.Setup(manager =>
            manager.FindByEmailAsync("ola@example.com")
        ).ReturnsAsync(user);

        env.SignIn.Setup(manager =>
            manager.PasswordSignInAsync(
                user,
                It.IsAny<string>(),
                false,
                true
            )
        ).ReturnsAsync(IdentitySignInResult.LockedOut);

        var result = await env.Controller.Login(
            CreateLogin()
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Index", redirect.ActionName);

        Assert.Null(
            env.HttpContext.Session.GetString("UserEmail")
        );
    }

    // ========================================
    // TEST 11
    // UTLOGGING
    // ========================================

    [Fact]
    public async Task Logout_ShouldClearSession()
    {
        var env = new TestEnvironment();

        env.HttpContext.Session.SetString(
            "UserEmail",
            "ola@example.com"
        );

        env.HttpContext.Session.SetString(
            "UserRole",
            IdentitySeeder.ResourceProviderRole
        );

        env.SignIn.Setup(manager =>
            manager.SignOutAsync()
        ).Returns(Task.CompletedTask);

        var result = await env.Controller.Logout();

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);

        env.SignIn.Verify(manager =>
            manager.SignOutAsync(),
            Times.Once
        );

        Assert.Empty(env.HttpContext.Session.Keys);
    }

    // ========================================
    // TEST 12
    // PROFIL HENTER BRUKERDATA
    // ========================================

    [Fact]
    public async Task Profile_Get_ShouldReturnCurrentUser()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetCurrentUser(user);

        var result = await env.Controller.Profile();

        var view = Assert.IsType<ViewResult>(result);

        var model =
            Assert.IsType<ProfileViewModel>(view.Model);

        Assert.Equal(user.Name, model.Name);
        Assert.Equal(user.Email, model.Email);
        Assert.Equal("+47", model.PhoneCountryCode);
        Assert.Equal("12345678", model.PhoneNumber);
    }

    // ========================================
    // TEST 13
    // OPPDATERING AV NAVN OG TELEFON
    // ========================================

    [Fact]
    public async Task Profile_Update_ShouldSaveNameAndPhone()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetCurrentUser(user);
        env.AllowProfileUpdate();

        var originalUserId = user.Id;

        var model = CreateProfile(
            name: " Kari Nordmann ",
            phoneNumber: "1234-5678"
        );

        var result = await env.Controller.Profile(model);

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Profile", redirect.ActionName);
        Assert.Equal("Kari Nordmann", user.Name);
        Assert.Equal("+4712345678", user.PhoneNumber);
        Assert.Equal("ola@example.com", user.Email);
        Assert.Equal(originalUserId, user.Id);

        env.Users.Verify(manager =>
            manager.UpdateAsync(user),
            Times.Once
        );

        env.SignIn.Verify(manager =>
            manager.RefreshSignInAsync(user),
            Times.Once
        );
    }

    // ========================================
    // TEST 14
    // E-POSTENDRING BEHOLDER IDENTITY-ID
    // ========================================

    [Fact]
    public async Task Profile_EmailChange_ShouldKeepUserId()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetCurrentUser(user);
        env.AllowProfileUpdate();

        var originalId = user.Id;

        env.Users.Setup(manager =>
            manager.FindByEmailAsync("ny@example.com")
        ).ReturnsAsync((ApplicationUser?)null);

        env.Users.Setup(manager =>
            manager.FindByNameAsync("ny@example.com")
        ).ReturnsAsync((ApplicationUser?)null);

        var model = CreateProfile(
            email: "ny@example.com"
        );

        var result = await env.Controller.Profile(model);

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Profile", redirect.ActionName);
        Assert.Equal("ny@example.com", user.Email);
        Assert.Equal("ny@example.com", user.UserName);
        Assert.Equal(originalId, user.Id);
        Assert.False(user.EmailConfirmed);

        Assert.Equal(
            "ny@example.com",
            env.HttpContext.Session.GetString("UserEmail")
        );

        env.Users.Verify(manager =>
            manager.UpdateAsync(user),
            Times.Once
        );
    }

    // ========================================
    // TEST 15
    // OPPTATT E-POST AVVISES
    // ========================================

    [Fact]
    public async Task Profile_DuplicateEmail_ShouldNotUpdate()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetCurrentUser(user);

        var otherUser = new ApplicationUser
        {
            Id = "another-user-456",
            Email = "opptatt@example.com",
            UserName = "opptatt@example.com"
        };

        env.Users.Setup(manager =>
            manager.FindByEmailAsync("opptatt@example.com")
        ).ReturnsAsync(otherUser);

        var model = CreateProfile(
            email: "opptatt@example.com"
        );

        var result = await env.Controller.Profile(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(env.Controller.ModelState.IsValid);

        env.Users.Verify(manager =>
            manager.UpdateAsync(
                It.IsAny<ApplicationUser>()
            ),
            Times.Never
        );

        Assert.Equal("ola@example.com", user.Email);
    }

    // ========================================
    // TEST 16
    // UGYLDIG PROFIL LAGRES IKKE
    // ========================================

    [Fact]
    public async Task Profile_InvalidModelState_ShouldNotUpdate()
    {
        var env = new TestEnvironment();

        env.SetCurrentUser(CreateUser());

        env.Controller.ModelState.AddModelError(
            "Name",
            "Navn er påkrevd."
        );

        var result = await env.Controller.Profile(
            CreateProfile()
        );

        Assert.IsType<ViewResult>(result);

        env.Users.Verify(manager =>
            manager.UpdateAsync(
                It.IsAny<ApplicationUser>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 17
    // MISLYKKET LAGRING OPPDATERER IKKE COOKIE
    // ========================================

    [Fact]
    public async Task Profile_UpdateFails_ShouldNotRefreshSignIn()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetCurrentUser(user);

        env.Users.Setup(manager =>
            manager.UpdateAsync(user)
        ).ReturnsAsync(
            IdentityResult.Failed(
                new IdentityError
                {
                    Description = "Kunne ikke lagre profilen."
                }
            )
        );

        var result = await env.Controller.Profile(
            CreateProfile(name: "Nytt navn")
        );

        Assert.IsType<ViewResult>(result);
        Assert.False(env.Controller.ModelState.IsValid);

        env.SignIn.Verify(manager =>
            manager.RefreshSignInAsync(
                It.IsAny<ApplicationUser>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 18
    // AUTORISASJON OG ANTIFORGERY
    // ========================================

    [Fact]
    public void AccountActions_ShouldHaveSecurityAttributes()
    {
        var postActions = new[]
        {
            typeof(AccountController).GetMethod(
                nameof(AccountController.Login),
                new[] { typeof(LoginViewModel) }
            ),

            typeof(AccountController).GetMethod(
                nameof(AccountController.Register),
                new[] { typeof(RegisterViewModel) }
            ),

            typeof(AccountController).GetMethod(
                nameof(AccountController.Logout),
                Type.EmptyTypes
            ),

            typeof(AccountController).GetMethod(
                nameof(AccountController.Profile),
                new[] { typeof(ProfileViewModel) }
            )
        };

        foreach (var method in postActions)
        {
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

        var profileGet = typeof(AccountController).GetMethod(
            nameof(AccountController.Profile),
            Type.EmptyTypes
        );

        var profilePost = typeof(AccountController).GetMethod(
            nameof(AccountController.Profile),
            new[] { typeof(ProfileViewModel) }
        );

        Assert.NotNull(profileGet);
        Assert.NotNull(profilePost);

        Assert.NotNull(
            profileGet.GetCustomAttribute<AuthorizeAttribute>()
        );

        Assert.NotNull(
            profilePost.GetCustomAttribute<AuthorizeAttribute>()
        );
    }
}