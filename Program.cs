using System.Globalization;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);


// =========================
// DATABASE
// =========================

string connectionString =
    builder.Configuration.GetConnectionString(
        "DefaultConnection"
    )
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' ble ikke funnet."
    );


builder.Services.AddDbContext<ApplicationDbContext>(
    options =>
        options.UseMySql(
            connectionString,
            new MariaDbServerVersion(
                new Version(11, 4, 0)
            )
        )
);


// =========================
// ASP.NET CORE IDENTITY
// =========================

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(
        options =>
        {
            // Hver e-postadresse skal være unik.
            options.User.RequireUniqueEmail = true;


            // Passordkrav.
            options.Password.RequiredLength = 8;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;


            // E-postbekreftelse kan aktiveres senere.
            options.SignIn.RequireConfirmedEmail = false;


            // Beskyttelse mot brute-force.
            options.Lockout.AllowedForNewUsers = true;

            options.Lockout.MaxFailedAccessAttempts = 5;

            options.Lockout.DefaultLockoutTimeSpan =
                TimeSpan.FromMinutes(10);
        }
    )
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();


// =========================
// IDENTITY COOKIE
// =========================

builder.Services.ConfigureApplicationCookie(
    options =>
    {
        // Innlogging ligger på hovedsiden.
        options.LoginPath = "/";

        options.AccessDeniedPath =
            "/Account/AccessDenied";

        options.Cookie.HttpOnly = true;

        options.Cookie.IsEssential = true;

        options.ExpireTimeSpan =
            TimeSpan.FromMinutes(60);

        options.SlidingExpiration = true;
    }
);


// =========================
// MVC
// =========================

builder.Services.AddControllersWithViews();


// =========================
// SESSION
// =========================
//
// Session beholdes midlertidig fordi deler
// av eksisterende dashboard fortsatt bruker den.
//
// Tilgangskontroll skal styres av
// ASP.NET Core Identity og roller.
//

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(
    options =>
    {
        options.IdleTimeout =
            TimeSpan.FromMinutes(30);

        options.Cookie.HttpOnly =
            true;

        options.Cookie.IsEssential =
            true;
    }
);


// =========================
// SPRÅK / KULTUR
// =========================

builder.Services.Configure<RequestLocalizationOptions>(
    options =>
    {
        var supportedCultures =
            new[]
            {
                new CultureInfo("en-US")
            };


        options.DefaultRequestCulture =
            new RequestCulture(
                "en-US"
            );

        options.SupportedCultures =
            supportedCultures;

        options.SupportedUICultures =
            supportedCultures;
    }
);


var app = builder.Build();


// =========================
// DATABASE MIGRATIONS
// =========================
//
// Kjører alle ventende EF Core-migrasjoner
// automatisk når applikasjonen starter.
//
// På en ny PC / ny Docker-volume vil dette
// opprette databasestrukturen automatisk.
//
// På en eksisterende database kjøres kun
// migrasjoner som ikke allerede er registrert
// i __EFMigrationsHistory.
//

using (var scope = app.Services.CreateScope())
{
    var dbContext =
        scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

    await dbContext.Database.MigrateAsync();
}


// =========================
// IDENTITY ROLE SEEDING
// =========================
//
// Sørger for at rollene:
// - Admin
// - PublicActor
// - ResourceProvider
//
// finnes i AspNetRoles.
//
// Seeder også eventuelle brukere som er
// definert i IdentitySeeder.
//
// Hvis dataene allerede finnes,
// skjer ingenting.
//

await IdentitySeeder.SeedAsync(
    app.Services
);


// =========================
// ERROR HANDLING
// =========================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(
        "/Home/Error"
    );

    app.UseHsts();
}


// =========================
// LOCALIZATION
// =========================

var localizationOptions =
    app.Services
        .GetRequiredService<
            Microsoft.Extensions.Options.IOptions<
                RequestLocalizationOptions
            >
        >()
        .Value;


app.UseRequestLocalization(
    localizationOptions
);


// =========================
// MIDDLEWARE
// =========================

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();


// Eksisterende Session-løsning.
app.UseSession();


// Identity autentisering.
//
// Authentication må alltid komme
// før Authorization.
app.UseAuthentication();

app.UseAuthorization();


// =========================
// ROUTING
// =========================

app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Home}/{action=Index}/{id?}"
);


app.Run();