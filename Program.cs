
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

// HTTPS-krevende cookies i produksjon.
// Lokal Docker-utvikling med HTTP skal fortsatt fungere.
CookieSecurePolicy cookieSecurePolicy =
    builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;

#region Sikkerhet og webserver

// Skjul servertypen i HTTP-responsen.
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

#endregion

#region Database

string connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' ble ikke funnet.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        new MariaDbServerVersion(new Version(11, 4, 0))));

#endregion

#region ASP.NET Core Identity

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;

        // Passordkrav.
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        // Eksisterende AccountController kontrollerer e-postbekreftelse.
        // Endres først når hele innloggings- og registreringsflyten er gjennomgått.
        options.SignIn.RequireConfirmedEmail = false;

        // Kontoen kan låses etter gjentatte feilforsøk.
        // Innloggingshandlingen må også bruke lockoutOnFailure: true.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    // Behold eksisterende tokenleverandør for e-postbasert tofaktor.
    .AddTokenProvider<UniqueEmailTokenProvider>(
        TokenOptions.DefaultEmailProvider);

#endregion

#region Autentiseringscookie

builder.Services.ConfigureApplicationCookie(options =>
{
    // Innloggingen skjer på forsiden i den eksisterende løsningen.
    options.LoginPath = "/";
    options.AccessDeniedPath = "/Account/AccessDenied";

    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;

    // Krev HTTPS utenfor utviklingsmiljøet.
    options.Cookie.SecurePolicy = cookieSecurePolicy;

    // Vanlig innlogging bruker en sesjonscookie med 60 minutters billettlevetid.
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
    options.SlidingExpiration = true;

    // En vedvarende cookie brukes bare når brukeren velger «Husk meg».
    options.Events.OnSigningIn = context =>
    {
        if (context.Properties.IsPersistent)
        {
            context.Properties.ExpiresUtc =
                DateTimeOffset.UtcNow.AddDays(7);
        }

        return Task.CompletedTask;
    };
});

#endregion

#region MVC

// Global CSRF-validering aktiveres etter at POST-kall
// fra skjemaer, JavaScript og Sami er gjennomgått.
builder.Services.AddControllersWithViews();

// Sikre antiforgery-cookien i miljøer som skal bruke HTTPS.
// Dette aktiverer ikke global CSRF-validering alene.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });
}

#endregion

#region Matching, varsling og rapportering

builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection("Smtp"));

builder.Services.AddScoped<IPortalEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<ISmsSender, SimulatedSmsSender>();

builder.Services.AddScoped<NotificationService>();

// Repository Pattern:
// Matchingservicen bruker et interface i stedet for
// å være direkte avhengig av ApplicationDbContext.
builder.Services.AddScoped<
    IResourceMatchingRepository,
    EfResourceMatchingRepository>();

builder.Services.AddScoped<ResourceMatchingService>();
builder.Services.AddScoped<AssignmentService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<FirstWebAppInDocker.Services.Reports.ReportService>();

// Brukes til bekreftelse av e-post, e-postendring,
// tofaktor og passordtilbakestilling.
builder.Services.AddScoped<IEmailCodeSender, SmtpEmailCodeSender>();

#endregion

#region Sami AI-assistent

builder.Services.Configure<SamiOptions>(
    builder.Configuration.GetSection("Sami"));

builder.Services.AddHttpClient<OllamaChatClient>();
builder.Services.AddHttpClient<ClaudeChatClient>();
builder.Services.AddHttpClient<NominatimGeocoder>();

// Kartverket brukes til adresseoppslag, med Nominatim som reserve.
builder.Services.AddHttpClient<IAddressLookup, KartverketAddressLookup>();

builder.Services.AddScoped<IChatModelClient>(services =>
{
    SamiOptions options = services
        .GetRequiredService<IOptions<SamiOptions>>()
        .Value;

    return string.Equals(
        options.Provider,
        "Claude",
        StringComparison.OrdinalIgnoreCase)
        ? (IChatModelClient)services.GetRequiredService<ClaudeChatClient>()
        : services.GetRequiredService<OllamaChatClient>();
});

builder.Services.AddScoped<SamiTools>();
builder.Services.AddScoped<SamiAssistantService>();

#endregion

#region Cache og sesjon

builder.Services.AddDistributedMemoryCache();

// Session brukes til eksisterende sesjonsdata,
// ikke som erstatning for Identity.
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;

    // Krev HTTPS utenfor utviklingsmiljøet.
    options.Cookie.SecurePolicy = cookieSecurePolicy;
});

#endregion

#region Lokalisering

// Behold en-US til dato- og tallformater
// i den eksisterende løsningen er gjennomgått.
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { new CultureInfo("en-US") };

    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

#endregion

var app = builder.Build();

#region Migrasjoner og oppstartsdata

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider
        .GetRequiredService<ApplicationDbContext>();

    await dbContext.Database.MigrateAsync();
}

await IdentitySeeder.SeedAsync(app.Services);

// Demodata skal bare opprettes i utviklingsmiljø
// og kan slås av i konfigurasjonen.
if (app.Environment.IsDevelopment()
    && !string.Equals(
        app.Configuration["Seed:DemoData"],
        "false",
        StringComparison.OrdinalIgnoreCase))
{
    await DemoDataSeeder.SeedAsync(app.Services);
}

#endregion

#region Feilhåndtering og sikkerhetsheadere

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Disse headerne kan brukes uten å blokkere
// Leaflet, Kartverket eller Sami.
// Content-Security-Policy innføres separat etter
// kartlegging av eksterne ressurser.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    await next();
});

#endregion

#region Middleware

var localizationOptions = app.Services
    .GetRequiredService<IOptions<RequestLocalizationOptions>>()
    .Value;

app.UseRequestLocalization(localizationOptions);
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

#endregion

#region Endepunkter

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

#endregion

app.Run();
