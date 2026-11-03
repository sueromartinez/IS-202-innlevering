using System.Globalization;
using FirstWebAppInDocker.Data;
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
// MVC
// =========================

builder.Services.AddControllersWithViews();


// =========================
// SESSION
// =========================

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
// OPPRETT NEEDS-TABELLEN
// =========================
// Prosjektet bruker ikke migrations, så vi sørger for at
// Needs-tabellen finnes når appen starter. Eksisterende
// tabeller og data blir ikke berørt.

using (var scope = app.Services.CreateScope())
{
    var db =
        scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();

    db.Database.ExecuteSqlRaw(
        @"CREATE TABLE IF NOT EXISTS Needs (
            Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            Title VARCHAR(100) NOT NULL,
            ResourceType INT NOT NULL,
            Description VARCHAR(500) NOT NULL,
            Priority INT NOT NULL,
            Latitude DOUBLE NOT NULL,
            Longitude DOUBLE NOT NULL,
            OwnerEmail VARCHAR(150) NOT NULL,
            CreatedAt DATETIME(6) NOT NULL
        )"
    );

    db.Database.ExecuteSqlRaw(
        @"CREATE TABLE IF NOT EXISTS Resources (
            Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            ResourceType INT NOT NULL,
            Description VARCHAR(500) NOT NULL,
            Latitude DOUBLE NOT NULL,
            Longitude DOUBLE NOT NULL,
            ContactEmail VARCHAR(150) NULL,
            ContactPhoneCountryCode VARCHAR(5) NULL,
            ContactPhoneNumber VARCHAR(20) NULL,
            OwnerEmail VARCHAR(150) NOT NULL,
            CreatedAt DATETIME(6) NOT NULL
        )"
    );
}

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


// VIKTIG:
// Session må aktiveres før
// controllers/views prøver å bruke den.
app.UseSession();

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