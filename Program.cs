
using System.Globalization;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.Services.Ai;
using FirstWebAppInDocker.Services.Matching;
using FirstWebAppInDocker.Services.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ========================================
// DATABASE
// ========================================

string connectionString =
    builder.Configuration.GetConnectionString(
        "DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' ble ikke funnet.");

builder.Services.AddDbContext<ApplicationDbContext>(
    options =>
        options.UseMySql(
            connectionString,
            new MariaDbServerVersion(
                new Version(11, 4, 0)
            )
        )
);

// ========================================
// ASP.NET CORE IDENTITY
// ========================================

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(
        options =>
        {
            // Hver e-postadresse skal være unik.
            options.User.RequireUniqueEmail = true;

            // ========================================
            // PASSORDKRAV
            // ========================================

            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;

            // ========================================
            // E-POSTBEKREFTELSE
            // ========================================

            // AccountController kontrollerer allerede
            // bekreftet e-post ved vanlig innlogging.

            options.SignIn.RequireConfirmedEmail = false;

            // ========================================
            // BESKYTTELSE MOT BRUTE-FORCE
            // ========================================

            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;

            options.Lockout.DefaultLockoutTimeSpan =
                TimeSpan.FromMinutes(10);
        }
    )
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()

    // ========================================
    // UNIKE E-POSTKODER
    // ========================================

    // Beholder eksisterende løsning for
    // e-postbasert tofaktorautentisering.

    .AddTokenProvider<UniqueEmailTokenProvider>(
        TokenOptions.DefaultEmailProvider
    );

// ========================================
// IDENTITY COOKIE
// ========================================

builder.Services.ConfigureApplicationCookie(
    options =>
    {
        // Innloggingen ligger på forsiden.
        options.LoginPath = "/";

        options.AccessDeniedPath =
            "/Account/AccessDenied";

        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;

        // ========================================
        // VANLIG INNLOGGING
        // ========================================

        // Uten "Husk meg" brukes en sesjonscookie.
        // Autentiseringsbilletten varer i 60 minutter.
        //
        // Ved aktivitet kan varigheten fornyes
        // fordi SlidingExpiration er aktivert.

        options.ExpireTimeSpan =
            TimeSpan.FromMinutes(60);

        options.SlidingExpiration = true;

        // ========================================
        // HUSK MEG
        // ========================================

        // Identity sender IsPersistent = true
        // bare når "Husk meg" er valgt.
        //
        // Vi gir slike innlogginger en egen
        // varighet på 7 dager.
        //
        // Uten "Husk meg" beholder vi
        // standardvarigheten og sesjonscookie.

        options.Events.OnSigningIn = context =>
        {
            if (context.Properties.IsPersistent)
            {
                context.Properties.ExpiresUtc =
                    DateTimeOffset.UtcNow.AddDays(7);
            }

            return Task.CompletedTask;
        };
    }
);

// ========================================
// MVC
// ========================================

builder.Services.AddControllersWithViews();

// ========================================
// MATCHING OG VARSLING
// ========================================

builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection("Smtp")
);

builder.Services.AddScoped<
    IPortalEmailSender,
    SmtpEmailSender
>();

builder.Services.AddScoped<
    ISmsSender,
    SimulatedSmsSender
>();

builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<ResourceMatchingService>();
builder.Services.AddScoped<AssignmentService>();

// Audit-logg og hastegrad for behov.
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NeedUrgencyService>();
builder.Services.AddScoped<FirstWebAppInDocker.Services.Reports.ReportService>();

// ========================================
// SAMI (AI-ASSISTENT)
// ========================================

builder.Services.Configure<SamiOptions>(
    builder.Configuration.GetSection("Sami")
);

builder.Services.AddHttpClient<OllamaChatClient>();
builder.Services.AddHttpClient<ClaudeChatClient>();
builder.Services.AddHttpClient<NominatimGeocoder>();

// Adresseoppslag (Kartverket, med Nominatim som reserve) når Sami registrerer ressurser.
builder.Services.AddHttpClient<IAddressLookup, KartverketAddressLookup>();

builder.Services.AddScoped<IChatModelClient>(
    services =>
    {
        SamiOptions options =
            services
                .GetRequiredService<IOptions<SamiOptions>>()
                .Value;

        return string.Equals(
            options.Provider,
            "Claude",
            StringComparison.OrdinalIgnoreCase
        )
            ? (IChatModelClient)
                services.GetRequiredService<ClaudeChatClient>()
            : services.GetRequiredService<OllamaChatClient>();
    }
);

builder.Services.AddScoped<SamiTools>();
builder.Services.AddScoped<SamiAssistantService>();

// ========================================
// DISTRIBUTED CACHE
// ========================================

builder.Services.AddDistributedMemoryCache();

// ========================================
// SESSION
// ========================================

// Eksisterende Session-løsning beholdes.
// Identity og roller styrer tilgang.

builder.Services.AddSession(
    options =>
    {
        options.IdleTimeout =
            TimeSpan.FromMinutes(30);

        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    }
);

// ========================================
// SPRÅK / KULTUR
// ========================================

builder.Services.Configure<RequestLocalizationOptions>(
    options =>
    {
        var supportedCultures =
            new[]
            {
                new CultureInfo("en-US")
            };

        options.DefaultRequestCulture =
            new RequestCulture("en-US");

        options.SupportedCultures =
            supportedCultures;

        options.SupportedUICultures =
            supportedCultures;
    }
);

// ========================================
// E-POSTUTSENDING
// ========================================

// Brukes til:
// - E-postbekreftelse
// - E-postendring
// - Tofaktorkoder
// - Passordtilbakestilling

builder.Services.AddScoped<
    IEmailCodeSender,
    SmtpEmailCodeSender
>();

// ========================================
// BUILD APPLICATION
// ========================================

var app = builder.Build();

// ========================================
// DATABASE MIGRATIONS
// ========================================

using (var scope = app.Services.CreateScope())
{
    var dbContext =
        scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

    await dbContext.Database.MigrateAsync();
}

// ========================================
// IDENTITY ROLE SEEDING
// ========================================

await IdentitySeeder.SeedAsync(
    app.Services
);

// ========================================
// DEMODATA
// ========================================

// Beholder eksisterende demodata
// og offentlige testbrukere.

if (
    app.Environment.IsDevelopment()
    && !string.Equals(
        app.Configuration["Seed:DemoData"],
        "false",
        StringComparison.OrdinalIgnoreCase
    )
)
{
    await DemoDataSeeder.SeedAsync(
        app.Services
    );
}

// ========================================
// ERROR HANDLING
// ========================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Home/Error"
    );

    app.UseHsts();
}

// ========================================
// LOCALIZATION
// ========================================

var localizationOptions =
    app.Services
        .GetRequiredService<
            IOptions<RequestLocalizationOptions>
        >()
        .Value;

app.UseRequestLocalization(
    localizationOptions
);

// ========================================
// MIDDLEWARE
// ========================================

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// ========================================
// SESSION
// ========================================

app.UseSession();

// ========================================
// IDENTITY AUTHENTICATION
// ========================================

app.UseAuthentication();
app.UseAuthorization();

// ========================================
// ROUTING
// ========================================

app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Home}/{action=Index}/{id?}"
);

// ========================================
// START APPLICATION
// ========================================

app.Run();
