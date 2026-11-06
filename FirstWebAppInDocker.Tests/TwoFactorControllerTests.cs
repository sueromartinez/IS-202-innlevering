
using System.Reflection;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
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

using IdentitySignInResult =
    Microsoft.AspNetCore.Identity.SignInResult;

namespace FirstWebAppInDocker.Tests;

public class TwoFactorControllerTests
{
    // ========================================
    // HJELPER - TESTSESSION
    // ========================================

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();

        public bool IsAvailable => true;

        public string Id => "twofactor-test-session";

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
    // HJELPER - TESTMILJØ
    // ========================================

    private sealed class TestEnvironment
    {
        public Mock<UserManager<ApplicationUser>> Users { get; }

        public Mock<SignInManager<ApplicationUser>> SignIn { get; }

        public Mock<IEmailCodeSender> Emails { get; }

        public TwoFactorController Controller { get; }

        public DefaultHttpContext HttpContext { get; }

        public TestEnvironment()
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
                    IUserClaimsPrincipalFactory<ApplicationUser>>(),
                Options.Create(new IdentityOptions()),
                NullLogger<SignInManager<ApplicationUser>>.Instance,
                Mock.Of<IAuthenticationSchemeProvider>(),
                Mock.Of<IUserConfirmation<ApplicationUser>>()
            );

            Emails = new Mock<IEmailCodeSender>();

            Controller = new TwoFactorController(
                Users.Object,
                SignIn.Object,
                Emails.Object,
                NullLogger<TwoFactorController>.Instance
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

        // Brukeren som har skrevet riktig passord,
        // og nå venter på tofaktor.

        public void SetPendingUser(ApplicationUser user)
        {
            SignIn.Setup(manager =>
                manager.GetTwoFactorAuthenticationUserAsync()
            ).ReturnsAsync(user);
        }

        // Klargjør Identity og e-posttjenesten
        // for en vellykket utsending.

        public void AllowEmailCode(ApplicationUser user)
        {
            Users.Setup(manager =>
                manager.GetEmailAsync(user)
            ).ReturnsAsync(user.Email);

            Users.Setup(manager =>
                manager.IsEmailConfirmedAsync(user)
            ).ReturnsAsync(true);

            Users.Setup(manager =>
                manager.GenerateTwoFactorTokenAsync(
                    user,
                    TokenOptions.DefaultEmailProvider
                )
            ).ReturnsAsync("123456");

            Emails.Setup(sender =>
                sender.SendCodeAsync(
                    user.Email!,
                    "123456",
                    It.IsAny<CancellationToken>()
                )
            ).Returns(Task.CompletedTask);
        }

        // Gjør det mulig å opprette vanlig
        // Session etter godkjent tofaktor.

        public void AllowLegacySession(ApplicationUser user)
        {
            IList<string> roles = new List<string>
            {
                IdentitySeeder.ResourceProviderRole
            };

            Users.Setup(manager =>
                manager.GetRolesAsync(user)
            ).ReturnsAsync(roles);
        }
    }

    // ========================================
    // HJELPER - TESTBRUKER
    // ========================================

    private static ApplicationUser CreateUser()
    {
        return new ApplicationUser
        {
            Id = "twofactor-user-123",
            Name = "Ola Nordmann",
            UserName = "ola@example.com",
            Email = "ola@example.com",
            EmailConfirmed = true,
            TwoFactorEnabled = true,
            PhoneNumber = "+4712345678",
            CreatedAt = DateTime.UtcNow
        };
    }

    // ========================================
    // HJELPER - LOGIN MODEL
    // ========================================

    private static TwoFactorLoginViewModel CreateLoginModel(
        string code = "123456")
    {
        return new TwoFactorLoginViewModel
        {
            Code = code
        };
    }

    // ========================================
    // TEST 1
    // INGEN VENTENDE TOFAKTORBRUKER
    // ========================================

    [Fact]
    public async Task LoginGet_WithoutPendingUser_ShouldRedirect()
    {
        var env = new TestEnvironment();

        env.SignIn.Setup(manager =>
            manager.GetTwoFactorAuthenticationUserAsync()
        ).ReturnsAsync((ApplicationUser?)null);

        var result = await env.Controller.Login();

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);

        env.Emails.Verify(sender =>
            sender.SendCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 2
    // KODE SENDES AUTOMATISK
    // ========================================

    [Fact]
    public async Task LoginGet_ConfirmedEmail_ShouldSendCode()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);
        env.AllowEmailCode(user);

        var result = await env.Controller.Login();

        var view = Assert.IsType<ViewResult>(result);

        Assert.IsType<TwoFactorLoginViewModel>(view.Model);

        // Identity skal generere e-postkode.

        env.Users.Verify(manager =>
            manager.GenerateTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultEmailProvider
            ),
            Times.Once
        );

        // Koden sendes til brukerens e-post.

        env.Emails.Verify(sender =>
            sender.SendCodeAsync(
                "ola@example.com",
                "123456",
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );

        // Registrer at koden er sendt.

        Assert.Equal(
            user.Id,
            env.HttpContext.Session.GetString(
                "TwoFactorEmailSentToUser"
            )
        );

        Assert.NotNull(
            env.HttpContext.Session.GetString(
                "TwoFactorEmailLastSentUtc"
            )
        );

        // Vanlig innlogging er ikke fullført.

        Assert.Null(
            env.HttpContext.Session.GetString("UserEmail")
        );
    }

    // ========================================
    // TEST 3
    // OPPDATERING AV SIDEN SENDER IKKE NY KODE
    // ========================================

    [Fact]
    public async Task LoginGet_AlreadySent_ShouldNotSendAgain()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);
        env.AllowEmailCode(user);

        // Samme bruker har allerede fått kode.

        env.HttpContext.Session.SetString(
            "TwoFactorEmailSentToUser",
            user.Id
        );

        var result = await env.Controller.Login();

        Assert.IsType<ViewResult>(result);

        env.Users.Verify(manager =>
            manager.GenerateTwoFactorTokenAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>()
            ),
            Times.Never
        );

        env.Emails.Verify(sender =>
            sender.SendCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 4
    // UBEKREFTET E-POST FÅR IKKE KODE
    // ========================================

    [Fact]
    public async Task LoginGet_UnconfirmedEmail_ShouldNotSend()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        user.EmailConfirmed = false;

        env.SetPendingUser(user);

        env.Users.Setup(manager =>
            manager.GetEmailAsync(user)
        ).ReturnsAsync(user.Email);

        env.Users.Setup(manager =>
            manager.IsEmailConfirmedAsync(user)
        ).ReturnsAsync(false);

        var result = await env.Controller.Login();

        Assert.IsType<ViewResult>(result);

        Assert.False(env.Controller.ModelState.IsValid);

        env.Users.Verify(manager =>
            manager.GenerateTwoFactorTokenAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>()
            ),
            Times.Never
        );

        env.Emails.Verify(sender =>
            sender.SendCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 5
    // MANGLENDE E-POSTADRESSE
    // ========================================

    [Fact]
    public async Task LoginGet_MissingEmail_ShouldNotSend()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);

        env.Users.Setup(manager =>
            manager.GetEmailAsync(user)
        ).ReturnsAsync((string?)null);

        var result = await env.Controller.Login();

        Assert.IsType<ViewResult>(result);

        Assert.False(env.Controller.ModelState.IsValid);

        env.Emails.Verify(sender =>
            sender.SendCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 6
    // KODEGENERERING MISLYKKES
    // ========================================

    [Fact]
    public async Task LoginGet_NoGeneratedCode_ShouldNotSend()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);
        env.AllowEmailCode(user);

        env.Users.Setup(manager =>
            manager.GenerateTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultEmailProvider
            )
        ).ReturnsAsync(string.Empty);

        var result = await env.Controller.Login();

        Assert.IsType<ViewResult>(result);

        Assert.False(env.Controller.ModelState.IsValid);

        env.Emails.Verify(sender =>
            sender.SendCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );

        Assert.Null(
            env.HttpContext.Session.GetString(
                "TwoFactorEmailSentToUser"
            )
        );
    }

    // ========================================
    // TEST 7
    // SMTP-FEIL HÅNDTERES
    // ========================================

    [Fact]
    public async Task LoginGet_EmailSendFails_ShouldShowError()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);
        env.AllowEmailCode(user);

        // Simuler SMTP-feil.

        env.Emails.Setup(sender =>
            sender.SendCodeAsync(
                "ola@example.com",
                "123456",
                It.IsAny<CancellationToken>()
            )
        ).ThrowsAsync(
            new InvalidOperationException(
                "SMTP unavailable"
            )
        );

        var result = await env.Controller.Login();

        Assert.IsType<ViewResult>(result);

        Assert.False(env.Controller.ModelState.IsValid);

        // Ikke registrer en mislykket utsending.

        Assert.Null(
            env.HttpContext.Session.GetString(
                "TwoFactorEmailSentToUser"
            )
        );

        Assert.Null(
            env.HttpContext.Session.GetString("UserEmail")
        );
    }

    // ========================================
    // TEST 8
    // POST UTEN VENTENDE BRUKER
    // ========================================

    [Fact]
    public async Task LoginPost_WithoutPendingUser_ShouldRedirect()
    {
        var env = new TestEnvironment();

        env.SignIn.Setup(manager =>
            manager.GetTwoFactorAuthenticationUserAsync()
        ).ReturnsAsync((ApplicationUser?)null);

        var result = await env.Controller.Login(
            CreateLoginModel()
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);

        env.SignIn.Verify(manager =>
            manager.TwoFactorSignInAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 9
    // UGYLDIG MODELL AVVISES
    // ========================================

    [Fact]
    public async Task LoginPost_InvalidModel_ShouldNotVerify()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);

        env.Controller.ModelState.AddModelError(
            nameof(TwoFactorLoginViewModel.Code),
            "Koden må inneholde seks sifre."
        );

        var result = await env.Controller.Login(
            CreateLoginModel("123")
        );

        Assert.IsType<ViewResult>(result);

        env.SignIn.Verify(manager =>
            manager.TwoFactorSignInAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>()
            ),
            Times.Never
        );

        Assert.Empty(env.HttpContext.Session.Keys);
    }

    // ========================================
    // TEST 10
    // UBEKREFTET E-POST KAN IKKE VERIFISERES
    // ========================================

    [Fact]
    public async Task LoginPost_UnconfirmedEmail_ShouldNotSignIn()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);

        env.Users.Setup(manager =>
            manager.IsEmailConfirmedAsync(user)
        ).ReturnsAsync(false);

        var result = await env.Controller.Login(
            CreateLoginModel()
        );

        Assert.IsType<ViewResult>(result);

        Assert.False(env.Controller.ModelState.IsValid);

        env.SignIn.Verify(manager =>
            manager.TwoFactorSignInAsync(
                It.IsAny<string>(),
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
    // TEST 11
    // FEIL E-POSTKODE GIR IKKE INNLOGGING
    // ========================================

    [Fact]
    public async Task LoginPost_WrongCode_ShouldNotCreateSession()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);

        env.Users.Setup(manager =>
            manager.IsEmailConfirmedAsync(user)
        ).ReturnsAsync(true);

        env.SignIn.Setup(manager =>
            manager.TwoFactorSignInAsync(
                TokenOptions.DefaultEmailProvider,
                "123456",
                false,
                false
            )
        ).ReturnsAsync(IdentitySignInResult.Failed);

        var result = await env.Controller.Login(
            CreateLoginModel()
        );

        Assert.IsType<ViewResult>(result);

        Assert.False(env.Controller.ModelState.IsValid);

        Assert.Null(
            env.HttpContext.Session.GetString("UserEmail")
        );

        Assert.Null(
            env.HttpContext.Session.GetString("UserRole")
        );

        env.Users.Verify(manager =>
            manager.GetRolesAsync(
                It.IsAny<ApplicationUser>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 12
    // LÅST KONTO GIR IKKE INNLOGGING
    // ========================================

    [Fact]
    public async Task LoginPost_LockedOut_ShouldNotCreateSession()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);

        env.Users.Setup(manager =>
            manager.IsEmailConfirmedAsync(user)
        ).ReturnsAsync(true);

        env.SignIn.Setup(manager =>
            manager.TwoFactorSignInAsync(
                TokenOptions.DefaultEmailProvider,
                "123456",
                false,
                false
            )
        ).ReturnsAsync(IdentitySignInResult.LockedOut);

        var result = await env.Controller.Login(
            CreateLoginModel()
        );

        Assert.IsType<ViewResult>(result);

        Assert.False(env.Controller.ModelState.IsValid);

        Assert.Empty(env.HttpContext.Session.Keys);
    }

    // ========================================
    // TEST 13
    // RIKTIG E-POSTKODE GIR INNLOGGING
    // ========================================

    [Fact]
    public async Task LoginPost_ValidCode_ShouldCreateSession()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);
        env.AllowLegacySession(user);

        env.Users.Setup(manager =>
            manager.IsEmailConfirmedAsync(user)
        ).ReturnsAsync(true);

        env.SignIn.Setup(manager =>
            manager.TwoFactorSignInAsync(
                TokenOptions.DefaultEmailProvider,
                "123456",
                false,
                false
            )
        ).ReturnsAsync(IdentitySignInResult.Success);

        // Kontroller også at mellomrom rundt
        // innsendt kode blir fjernet.

        var result = await env.Controller.Login(
            CreateLoginModel(" 123456 ")
        );

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Dashboard", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);

        // Innlogging er nå fullført.

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
            manager.TwoFactorSignInAsync(
                TokenOptions.DefaultEmailProvider,
                "123456",
                false,
                false
            ),
            Times.Once
        );
    }

    // ========================================
    // TEST 14
    // SEND NY KODE UTEN VENTENDE BRUKER
    // ========================================

    [Fact]
    public async Task ResendCode_WithoutPendingUser_ShouldRedirect()
    {
        var env = new TestEnvironment();

        env.SignIn.Setup(manager =>
            manager.GetTwoFactorAuthenticationUserAsync()
        ).ReturnsAsync((ApplicationUser?)null);

        var result = await env.Controller.ResendCode();

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);

        env.Emails.Verify(sender =>
            sender.SendCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 15
    // VENTETID HINDRER FOR HYPPIG UTSENDING
    // ========================================

    [Fact]
    public async Task ResendCode_WithinCooldown_ShouldNotSend()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);
        env.AllowEmailCode(user);

        env.HttpContext.Session.SetString(
            "TwoFactorEmailSentToUser",
            user.Id
        );

        env.HttpContext.Session.SetString(
            "TwoFactorEmailLastSentUtc",
            DateTimeOffset.UtcNow
                .ToUnixTimeSeconds()
                .ToString()
        );

        var result = await env.Controller.ResendCode();

        var view = Assert.IsType<ViewResult>(result);

        Assert.Equal("Login", view.ViewName);

        Assert.False(env.Controller.ModelState.IsValid);

        // Ingen ny kode skal genereres.

        env.Users.Verify(manager =>
            manager.GenerateTwoFactorTokenAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>()
            ),
            Times.Never
        );

        // Ingen ny e-post skal sendes.

        env.Emails.Verify(sender =>
            sender.SendCodeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()
            ),
            Times.Never
        );
    }

    // ========================================
    // TEST 16
    // KODE KAN SENDES ETTER VENTETID
    // ========================================

    [Fact]
    public async Task ResendCode_AfterCooldown_ShouldSend()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);
        env.AllowEmailCode(user);

        // Simuler at forrige kode ble
        // sendt mer enn 60 sekunder siden.

        long previousSendTime =
            DateTimeOffset.UtcNow
                .ToUnixTimeSeconds() - 120;

        env.HttpContext.Session.SetString(
            "TwoFactorEmailSentToUser",
            user.Id
        );

        env.HttpContext.Session.SetString(
            "TwoFactorEmailLastSentUtc",
            previousSendTime.ToString()
        );

        var result = await env.Controller.ResendCode();

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Login", redirect.ActionName);

        env.Users.Verify(manager =>
            manager.GenerateTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultEmailProvider
            ),
            Times.Once
        );

        env.Emails.Verify(sender =>
            sender.SendCodeAsync(
                "ola@example.com",
                "123456",
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );

        // Tidspunktet skal være oppdatert.

        string? newTime =
            env.HttpContext.Session.GetString(
                "TwoFactorEmailLastSentUtc"
            );

        Assert.NotNull(newTime);

        Assert.True(
            long.Parse(newTime) > previousSendTime
        );
    }

    // ========================================
    // TEST 17
    // SMTP-FEIL VED NY KODE
    // ========================================

    [Fact]
    public async Task ResendCode_EmailFailure_ShouldShowError()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);
        env.AllowEmailCode(user);

        env.Emails.Setup(sender =>
            sender.SendCodeAsync(
                "ola@example.com",
                "123456",
                It.IsAny<CancellationToken>()
            )
        ).ThrowsAsync(
            new InvalidOperationException(
                "SMTP unavailable"
            )
        );

        var result = await env.Controller.ResendCode();

        var view = Assert.IsType<ViewResult>(result);

        Assert.Equal("Login", view.ViewName);

        Assert.False(env.Controller.ModelState.IsValid);

        // Ikke marker meldingen som sendt.

        Assert.Null(
            env.HttpContext.Session.GetString(
                "TwoFactorEmailSentToUser"
            )
        );

        // Ingen vanlig innlogging.

        Assert.Null(
            env.HttpContext.Session.GetString("UserEmail")
        );
    }

    // ========================================
    // TEST 18
    // VELLYKKET NY UTSENDING
    // ========================================

    [Fact]
    public async Task ResendCode_Success_ShouldRedirectToLogin()
    {
        var env = new TestEnvironment();
        var user = CreateUser();

        env.SetPendingUser(user);
        env.AllowEmailCode(user);

        var result = await env.Controller.ResendCode();

        var redirect =
            Assert.IsType<RedirectToActionResult>(result);

        Assert.Equal("Login", redirect.ActionName);

        env.Emails.Verify(sender =>
            sender.SendCodeAsync(
                "ola@example.com",
                "123456",
                It.IsAny<CancellationToken>()
            ),
            Times.Once
        );

        Assert.Equal(
            user.Id,
            env.HttpContext.Session.GetString(
                "TwoFactorEmailSentToUser"
            )
        );

        Assert.Null(
            env.HttpContext.Session.GetString("UserEmail")
        );
    }

    // ========================================
    // TEST 19
    // SIKKERHETSATTRIBUTTER
    // ========================================

    [Fact]
    public void TwoFactorActions_ShouldHaveSecurityAttributes()
    {
        var loginGet =
            typeof(TwoFactorController).GetMethod(
                nameof(TwoFactorController.Login),
                Type.EmptyTypes
            );

        var loginPost =
            typeof(TwoFactorController).GetMethod(
                nameof(TwoFactorController.Login),
                new[]
                {
                    typeof(TwoFactorLoginViewModel)
                }
            );

        var resendPost =
            typeof(TwoFactorController).GetMethod(
                nameof(TwoFactorController.ResendCode),
                Type.EmptyTypes
            );

        Assert.NotNull(loginGet);
        Assert.NotNull(loginPost);
        Assert.NotNull(resendPost);

        // Innloggingssidene er tilgjengelige
        // før brukeren har fullført tofaktor.

        Assert.NotNull(
            loginGet.GetCustomAttribute<
                AllowAnonymousAttribute
            >()
        );

        Assert.NotNull(
            loginPost.GetCustomAttribute<
                AllowAnonymousAttribute
            >()
        );

        Assert.NotNull(
            resendPost.GetCustomAttribute<
                AllowAnonymousAttribute
            >()
        );

        // GET skal bare lese informasjon.

        Assert.NotNull(
            loginGet.GetCustomAttribute<
                HttpGetAttribute
            >()
        );

        // POST-handlingene må være beskyttet
        // med antiforgery.

        foreach (var method in new[]
        {
            loginPost,
            resendPost
        })
        {
            Assert.NotNull(
                method.GetCustomAttribute<
                    HttpPostAttribute
                >()
            );

            Assert.NotNull(
                method.GetCustomAttribute<
                    ValidateAntiForgeryTokenAttribute
                >()
            );
        }

        // Kodesiden skal ikke caches.

        Assert.NotNull(
            loginGet.GetCustomAttribute<
                ResponseCacheAttribute
            >()
        );

        Assert.NotNull(
            loginPost.GetCustomAttribute<
                ResponseCacheAttribute
            >()
        );

        Assert.NotNull(
            resendPost.GetCustomAttribute<
                ResponseCacheAttribute
            >()
        );
    }
}
