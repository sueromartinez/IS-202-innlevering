/*
    Program.cs – oppstarten av nettsiden

    Hva filen gjør: Dette er det første som kjøres når nettsiden starter. Filen gjør to ting.
    1) Den kobler sammen alle delene av programmet: databasen, innloggingen, Sami, matchingen,
       varslene og så videre. Dette heter "Dependency Injection": vi sier én gang hvilke klasser
       som finnes, og så får controllerne dem automatisk.
    2) Den bestemmer hvilke steg hver forespørsel går gjennom før den når en controller
       (sikkerhet, innlogging, statiske filer og så videre). Dette kalles middleware.

    På nettsiden: Ingenting vises direkte herfra, men alt brukeren ser er avhengig av at denne
    filen har satt opp databasen, innloggingen og sidene riktig.

    Oppgavekrav: 1, 7, 8, 9, 14
*/

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

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

/*
    Bestemmer om innloggings-cookien bare kan sendes over HTTPS. Lokalt i Docker kjører vi
    vanlig HTTP, så der tillater vi begge. Utenfor utviklingsmiljøet kreves HTTPS.
*/
CookieSecurePolicy cookieSecurePolicy =
    builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;

#region Sikkerhet og webserver

/*
    Kestrel er webserveren som følger med ASP.NET Core. Vi skjuler navnet på serveren i svarene,
    så en angriper ikke ser hva vi kjører.
*/
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
});

#endregion

#region Database

/*
    Henter adressen og passordet til databasen fra innstillingene (appsettings.json eller .env
    via docker-compose). Passordet står altså ikke i koden. Mangler det, stopper appen med en
    tydelig feilmelding.
*/
string connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' ble ikke funnet.");

/*
    Kobler appen til MariaDB-databasen via EF Core.
    Hvorfor MariaDB: gratis, åpen kildekode og enkel å kjøre i Docker. Fordi vi bruker EF Core,
    er det i hovedsak bare denne linjen som måtte endres om vi byttet til en annen database.
*/
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 4, 0))));

#endregion

#region ASP.NET Core Identity

builder.Services
    /*
        Slår på ASP.NET Core Identity. Identity tar seg av brukerkontoer, roller, sikker lagring
        av passord (hashing), låsing etter mange feil passord og engangskoder på e-post. Vi
        bruker det ferdige rammeverket i stedet for å lage alt selv, fordi det er lett å gjøre
        feil med sikkerhet.
        På nettsiden: dette er det som gjør at man kan registrere seg, logge inn og bare se
        sidene rollen sin har tilgang til.
    */
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;

        /*
            Krav til passord: minst 8 tegn, med stor og liten bokstav, tall og spesialtegn.
            På nettsiden: registreringsskjemaet viser en feilmelding hvis passordet er for
            svakt.
        */
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        /*
            Eksisterende AccountController kontrollerer e-postbekreftelse.
            Endres først når hele innloggings- og registreringsflyten er gjennomgått.
        */
        options.SignIn.RequireConfirmedEmail = false;

        /*
            Etter 5 feil passord låses kontoen i 10 minutter. Det stopper programmer som prøver
            tusenvis av passord automatisk.
        */
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    // Vår egen klasse for 6-sifrede engangskoder på e-post (brukes ved tofaktor-innlogging).
    .AddTokenProvider<UniqueEmailTokenProvider>(TokenOptions.DefaultEmailProvider);

#endregion

#region Autentiseringscookie

/*
    Innstillinger for innloggings-cookien, altså det som husker at du er logget inn.
    - HttpOnly: JavaScript kan ikke lese cookien, så den kan ikke stjeles med skadelig script.
    - SameSite=Lax: cookien sendes ikke med skjemaer fra andre nettsider.
    - Utløper etter 60 minutter uten aktivitet.
    På nettsiden: er man ikke logget inn, sendes man til forsiden (LoginPath). Prøver man å åpne
    en side rollen ikke har tilgang til, sendes man til AccessDenied.
*/
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

    /*
        Hvis brukeren huker av «Husk meg» på innloggingen, varer cookien i 7 dager. Ellers
        forsvinner den når nettleseren lukkes.
    */
    options.Events.OnSigningIn = context =>
    {
        if (context.Properties.IsPersistent)
        {
            context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7);
        }

        return Task.CompletedTask;
    };
});

#endregion

#region MVC

/*
    Slår på MVC: Model (data), View (HTML-siden) og Controller (koden som tar imot forespørselen
    og velger hvilken side som skal vises). Sidene lages ferdig på serveren og sendes som HTML
    til nettleseren.
*/
builder.Services.AddControllersWithViews();

/*
    Sikre antiforgery-cookien i miljøer som skal bruke HTTPS.
    Dette aktiverer ikke global CSRF-validering alene.
*/
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });
}

#endregion

#region Matching, varsling og rapportering

builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));

/*
    Her registreres tjenestene. Koden ber om et interface (for eksempel ISmsSender), og her
    bestemmer vi hvilken klasse som brukes. SMS er foreløpig simulert. Skal vi sende ekte SMS,
    lager vi én ny klasse og bytter én linje her.
    AddScoped betyr ett objekt per forespørsel fra nettleseren.
*/
builder.Services.AddScoped<IPortalEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<ISmsSender, SimulatedSmsSender>();

builder.Services.AddScoped<NotificationService>();

/*
    Repository Pattern:
    Matchingservicen bruker et interface i stedet for
    å være direkte avhengig av ApplicationDbContext.
*/
builder.Services.AddScoped<
    /*
        Matchingen henter data gjennom et interface (Repository-mønsteret) i stedet for direkte
        fra databasen. Da er matchinglogikken ikke låst til EF Core.
    */
    IResourceMatchingRepository,
    EfResourceMatchingRepository>();

builder.Services.AddScoped<ResourceMatchingService>();
/*
    Logikken for forespørsler, revisjonslogg og rapporter ligger i egne service-klasser. Da kan
    både nettsiden og Sami bruke nøyaktig samme regler.
*/
builder.Services.AddScoped<AssignmentService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<FirstWebAppInDocker.Services.Reports.ReportService>();

/*
    Brukes til bekreftelse av e-post, e-postendring,
    tofaktor og passordtilbakestilling.
*/
builder.Services.AddScoped<IEmailCodeSender, SmtpEmailCodeSender>();

#endregion

#region Sami AI-assistent

builder.Services.Configure<SamiOptions>(builder.Configuration.GetSection("Sami"));

/*
    Klienter for eksterne tjenester (AI-modellen og Kartverkets adressesøk). AddHttpClient lar
    ASP.NET gjenbruke nettverkstilkoblingene på en trygg måte.
*/
builder.Services.AddHttpClient<OllamaChatClient>();
builder.Services.AddHttpClient<ClaudeChatClient>();
builder.Services.AddHttpClient<NominatimGeocoder>();

// Kartverket brukes til adresseoppslag, med Nominatim som reserve.
builder.Services.AddHttpClient<IAddressLookup, KartverketAddressLookup>();

/*
    Velger hvilken AI-modell Sami bruker, ut fra innstillingen Sami:Provider. "Claude" betyr
    Claude API (smartere og raskere, krever API-nøkkel). Ellers brukes Ollama, en lokal modell
    som er gratis og der ingen data forlater maskinen, men som er tregere.
*/
builder.Services.AddScoped<IChatModelClient>(services =>
{
    SamiOptions options = services
        .GetRequiredService<IOptions<SamiOptions>>()
        .Value;

    return string.Equals(options.Provider, "Claude", StringComparison.OrdinalIgnoreCase)
        ? (IChatModelClient)services.GetRequiredService<ClaudeChatClient>()
        : services.GetRequiredService<OllamaChatClient>();
});

builder.Services.AddScoped<SamiTools>();
builder.Services.AddScoped<SamiAssistantService>();

#endregion

#region Cache og sesjon

/*
    Session (korttidsminne per bruker) lagres i serverens minne. Brukes blant annet til navnet
    og rollen som vises i menyen.
*/
builder.Services.AddDistributedMemoryCache();

/*
    Session brukes til eksisterende sesjonsdata,
    ikke som erstatning for Identity.
*/
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

/*
    Vi bruker amerikansk tallformat (en-US). Med norsk format blir desimaltegnet komma (58,14),
    men kartet og JavaScript bruker punktum (58.14). Uten dette kunne koordinatene fra kartet
    blitt lest feil.
*/
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    CultureInfo[] supportedCultures = new[] { new CultureInfo("en-US") };

    options.DefaultRequestCulture = new RequestCulture("en-US");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});

#endregion

WebApplication app = builder.Build();

#region Migrasjoner og oppstartsdata

using (IServiceScope scope = app.Services.CreateScope())
{
    ApplicationDbContext dbContext = scope.ServiceProvider
        .GetRequiredService<ApplicationDbContext>();

    /*
        Oppdaterer tabellene i databasen automatisk når appen starter (EF Core-migrasjoner).
        Sensor trenger derfor ikke lage databasen selv – den bygges når containeren starter.
    */
    await dbContext.Database.MigrateAsync();
}

// Lager de tre rollene og en admin-bruker hvis de ikke finnes fra før.
await IdentitySeeder.SeedAsync(app.Services);

/*
    Demodata skal bare opprettes i utviklingsmiljø
    og kan slås av i konfigurasjonen.
*/
if (app.Environment.IsDevelopment()
    && !string.Equals(
        app.Configuration["Seed:DemoData"],
        "false",
        StringComparison.OrdinalIgnoreCase))
{
    /*
        Legger inn eksempelressurser og -behov, bare i utviklingsmiljøet, slik at nettsiden ikke
        er tom første gang man åpner den.
    */
    await DemoDataSeeder.SeedAsync(app.Services);
}

#endregion

#region Feilhåndtering og sikkerhetsheadere

if (!app.Environment.IsDevelopment())
{
    // I produksjon ser brukeren en enkel feilside i stedet for tekniske detaljer om koden.
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

/*
    Legger sikkerhetsinnstillinger på alle svar fra serveren:
    - nosniff: nettleseren skal ikke gjette filtype.
    - X-Frame-Options: siden kan ikke legges inni en annen nettside (beskytter mot
      klikk-kapring).
    - Referrer-Policy: begrenser hva som sendes videre når man klikker en lenke til en annen
      side.

    Disse headerne kan brukes uten å blokkere
    Leaflet, Kartverket eller Sami.
    Content-Security-Policy innføres separat etter
    kartlegging av eksterne ressurser.
*/
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    await next();
});

#endregion

#region Middleware

RequestLocalizationOptions localizationOptions = app.Services
    .GetRequiredService<IOptions<RequestLocalizationOptions>>()
    .Value;

/*
    Rekkefølgen her er viktig. Først statiske filer (CSS, JavaScript, bilder), så finner ASP.NET
    ut hvilken side som er bedt om, så hvem brukeren er (UseAuthentication), og til slutt om
    brukeren har lov (UseAuthorization).
*/
app.UseRequestLocalization(localizationOptions);
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

#endregion

#region Endepunkter

/*
    Bestemmer hvordan adresser oversettes til kode. /Resource/RegisterResource kjører metoden
    RegisterResource i ResourceController. Uten noe bak adressen vises forsiden (Home/Index).
*/
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

#endregion

app.Run();
