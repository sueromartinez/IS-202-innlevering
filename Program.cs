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