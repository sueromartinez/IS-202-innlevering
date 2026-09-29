// Gjør at vi kan bruke funksjoner for språk og kultur,
// for eksempel RequestLocalizationOptions og RequestCulture.
using Microsoft.AspNetCore.Localization;


// Oppretter en builder som brukes til å konfigurere webapplikasjonen.
// args inneholder eventuelle argumenter som sendes inn når programmet starter.
var builder = WebApplication.CreateBuilder(args);


// Legger til støtte for Controllers og Views.
// Dette brukes når vi lager en ASP.NET Core MVC-applikasjon.
builder.Services.AddControllersWithViews();


// Lager en liste over språk/kulturer som nettsiden skal støtte.
// Her støtter applikasjonen bare amerikansk engelsk (en-US).
var supportedCultures = new[]
{
    new System.Globalization.CultureInfo("en-US")
};


// Konfigurerer språk og kultur for applikasjonen.
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    // Bestemmer standardspråket som skal brukes.
    // Hvis ikke noe annet språk er valgt, brukes en-US.
    options.DefaultRequestCulture = new RequestCulture("en-US");

    // Bestemmer hvilke kulturer applikasjonen støtter.
    // Kultur påvirker blant annet dato, klokkeslett, tall og valuta.
    options.SupportedCultures = supportedCultures;

    // Bestemmer hvilke språk som støttes for tekst i brukergrensesnittet.
    options.SupportedUICultures = supportedCultures;
});


// Bygger selve webapplikasjonen basert på innstillingene ovenfor.
var app = builder.Build();


// Aktiverer språk- og kulturinnstillingene vi konfigurerte tidligere.
// Dette gjør at ASP.NET Core bruker riktig kultur når en request kommer inn.
app.UseRequestLocalization();


// Sjekker om applikasjonen IKKE kjører i Development-modus.
// Denne delen brukes altså vanligvis når nettsiden kjører i produksjon.
if (!app.Environment.IsDevelopment())
{
    // Hvis det oppstår en feil, sendes brukeren til
    // Error-action i HomeController.
    app.UseExceptionHandler("/Home/Error");

    // Aktiverer HSTS (HTTP Strict Transport Security).
    // Dette forteller nettleseren at nettsiden skal bruke HTTPS.
    app.UseHsts();
}


// Sender HTTP-forespørsler videre til HTTPS.
// For eksempel kan http://example.com bli sendt til https://example.com.
app.UseHttpsRedirection();


// Aktiverer routing.
// Routing bestemmer hvilken Controller og Action som skal håndtere en URL.
app.UseRouting();


// Aktiverer autorisasjon.
// Dette brukes for å kontrollere om en bruker har tilgang
// til bestemte sider eller funksjoner.
app.UseAuthorization();


// Gjør statiske filer tilgjengelige,
// for eksempel CSS, JavaScript, bilder og andre filer.
app.MapStaticAssets();


// Lager standard routing for Controllers.
app.MapControllerRoute(
    // Navnet på denne ruten.
    name: "default",

    // Bestemmer hvordan URL-en skal bygges opp.
    // Eksempel: /Home/Index/5
    //
    // Hvis ingen controller blir skrevet i URL-en,
    // brukes HomeController som standard.
    //
    // Hvis ingen action blir skrevet,
    // brukes Index som standard.
    //
    // id? betyr at id er valgfri.
    pattern: "{controller=Home}/{action=Index}/{id?}")

    // Knytter statiske filer til denne routingen.
    .WithStaticAssets();


// Starter webapplikasjonen.
// Programmet begynner nå å lytte etter HTTP/HTTPS-forespørsler.
app.Run();