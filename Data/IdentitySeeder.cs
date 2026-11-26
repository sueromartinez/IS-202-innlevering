/*
    IdentitySeeder.cs – roller og standardbrukere

    Hva filen gjør: Kjøres når appen starter. Lager de tre rollene (Admin, PublicActor,
    ResourceProvider), en admin-bruker med e-post og passord fra .env, og en testbruker for
    offentlig aktør. Rollenavnene er konstanter her, så [Authorize(Roles =
    IdentitySeeder.AdminRole)] i controllerne ikke kan ha skrivefeil.

    På nettsiden: Gjør at sensor kan logge inn som admin eller offentlig aktør med en gang (se
    readme.md).

    Oppgavekrav: 4, 8
*/

using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;

namespace FirstWebAppInDocker.Data;

/*
    Oppretter nødvendige Identity-roller og konfigurerte oppstartskontoer.
    Testbrukere opprettes kun i utviklingsmiljøet.
*/
public static class IdentitySeeder
{
    #region Konstanter og felt

    // Rollen for portalens administratorer.
    public const string AdminRole = "Admin";

    // Rollen for offentlige aktører.
    public const string PublicActorRole = "PublicActor";

    // Rollen for ressursleverandører.
    public const string ResourceProviderRole = "ResourceProvider";

    /*
        E-postadressen til den lokale demobrukeren.
        Brukes også av AccountController ved demoinnlogging.
    */
    public const string PublicActorEmail = "offentlig@beredskap.no";

    #endregion

    #region Oppstart

    /*
        Oppretter manglende roller, eventuell utviklingsbruker
        og administrator når administrator er konfigurert.

        services: Applikasjonens tjenesteleverandør.
        Returnerer: En oppgave som fullføres når oppstartsdata er kontrollert.
    */
    public static async Task SeedAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();

        RoleManager<IdentityRole> roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole>>();

        UserManager<ApplicationUser> userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        IConfiguration configuration = scope.ServiceProvider
            .GetRequiredService<IConfiguration>();

        IWebHostEnvironment environment = scope.ServiceProvider
            .GetRequiredService<IWebHostEnvironment>();

        ILogger logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(IdentitySeeder));

        await SeedRolesAsync(roleManager);

        // Samme bryter som Program.cs bruker for utviklingsdata.
        bool seedDemoData = environment.IsDevelopment()
            && !string.Equals(
                configuration["Seed:DemoData"],
                "false",
                StringComparison.OrdinalIgnoreCase);

        if (seedDemoData)
        {
            await SeedPublicActorAsync(userManager, configuration, logger);
        }
        else if (!environment.IsDevelopment())
        {
            // Tidligere oppstartsdata fjernes ikke automatisk.
            ApplicationUser? oldDemoUser = await userManager.FindByEmailAsync(PublicActorEmail);

            if (oldDemoUser is not null)
            {
                logger.LogWarning(
                    "Utviklingsbrukeren {UserId} finnes fortsatt. " +
                    "Kontroller at den er deaktivert eller fjernet før produksjonssetting.",
                    oldDemoUser.Id);
            }
        }

        await SeedAdminAsync(userManager, configuration, environment, logger);
    }

    #endregion

    #region Roller og brukere

    /*
        Oppretter de tre faste rollene dersom de mangler.
        Eksisterende roller endres ikke.

        roleManager: Identity sin rolleadministrator.
        Returnerer: En oppgave som fullføres når rollene er kontrollert.
    */
    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        string[] roles =
        {
            AdminRole,
            PublicActorRole,
            ResourceProviderRole
        };

        foreach (string roleName in roles)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            IdentityResult result = await roleManager.CreateAsync(new IdentityRole(roleName));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Kunne ikke opprette rollen '{roleName}': " +
                    FormatIdentityErrors(result));
            }
        }
    }

    /*
        Oppretter en offentlig demobruker for lokal utvikling.
        Demoinnloggingen i AccountController trenger ikke passordet.

        userManager: Identity sin brukeradministrator.
        configuration: Applikasjonens konfigurasjon.
        logger: Logger for oppstartshendelser.
        Returnerer: En oppgave som fullføres når demobrukeren er kontrollert.
    */
    private static async Task SeedPublicActorAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger logger)
    {
        ApplicationUser? publicActor = await userManager.FindByEmailAsync(PublicActorEmail);

        if (publicActor is not null)
        {
            /*
                Ikke gi en eksisterende ordinær konto en høyere rolle
                bare fordi e-postadressen stemmer med demokontoen.
            */
            if (!await userManager.IsInRoleAsync(publicActor, PublicActorRole))
            {
                throw new InvalidOperationException(
                    "E-postadressen for offentlig demobruker er allerede i bruk " +
                    "av en konto uten PublicActor-rollen.");
            }

            return;
        }

        /*
            Ingen kjent standardpassord lagres i kildekoden.
            Ved manglende konfigurasjon får demobrukeren et tilfeldig passord.
        */
        string? configuredPassword = configuration["Seed:PublicActorPassword"];

        string password = !string.IsNullOrWhiteSpace(configuredPassword)
            ? configuredPassword
            : Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)) + "Aa1!";

        ApplicationUser user = new ApplicationUser
        {
            UserName = PublicActorEmail,
            Email = PublicActorEmail,
            Name = "Beredskapsbruker",
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        IdentityResult createResult = await userManager.CreateAsync(user, password);

        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Kunne ikke opprette offentlig demobruker: " +
                FormatIdentityErrors(createResult));
        }

        IdentityResult roleResult = await userManager.AddToRoleAsync(user, PublicActorRole);

        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);

            throw new InvalidOperationException(
                "Kunne ikke tildele PublicActor-rollen: " +
                FormatIdentityErrors(roleResult));
        }

        logger.LogInformation(
            "Offentlig demobruker {UserId} er opprettet for lokal utvikling.",
            user.Id);
    }

    /*
        Oppretter en administrator fra konfigurasjon uten standardpassord.
        Eksisterende administratorers passord endres ikke ved oppstart.

        userManager: Identity sin brukeradministrator.
        configuration: Applikasjonens konfigurasjon.
        environment: Gjeldende kjøremiljø.
        logger: Logger for oppstartshendelser.
        Returnerer: En oppgave som fullføres når administrator er kontrollert.
    */
    private static async Task SeedAdminAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger logger)
    {
        string? configuredEmail = configuration["Seed:AdminEmail"];
        string? configuredPassword = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(configuredEmail)
            && string.IsNullOrWhiteSpace(configuredPassword))
        {
            logger.LogInformation("Ingen administrator er konfigurert for automatisk opprettelse.");
            return;
        }

        if (string.IsNullOrWhiteSpace(configuredEmail)
            || string.IsNullOrWhiteSpace(configuredPassword))
        {
            throw new InvalidOperationException(
                "Både Seed:AdminEmail og Seed:AdminPassword må konfigureres.");
        }

        string adminEmail = configuredEmail.Trim();
        string adminPassword = configuredPassword;

        if (!new EmailAddressAttribute().IsValid(adminEmail))
        {
            throw new InvalidOperationException(
                "Seed:AdminEmail inneholder ikke en gyldig e-postadresse.");
        }

        /*
            Et kort oppstartspassord skal aldri godtas i produksjonsmiljø.
            I utvikling beholdes eksisterende Docker-oppsett inntil det ryddes.
        */
        if (!environment.IsDevelopment() && adminPassword.Length < 16)
        {
            throw new InvalidOperationException(
                "Seed:AdminPassword må være minst 16 tegn i produksjon.");
        }

        ApplicationUser? admin = await userManager.FindByEmailAsync(adminEmail);

        if (admin is not null)
        {
            // Unngå automatisk rolleopprykk av en konto som allerede finnes.
            if (!await userManager.IsInRoleAsync(admin, AdminRole))
            {
                throw new InvalidOperationException(
                    "Den konfigurerte administratoradressen tilhører en " +
                    "eksisterende konto uten Admin-rollen.");
            }

            if (!await userManager.GetTwoFactorEnabledAsync(admin))
            {
                logger.LogWarning(
                    "Administrator {UserId} mangler tofaktorautentisering. " +
                    "Dette må aktiveres før produksjonssetting.",
                    admin.Id);
            }

            return;
        }

        ApplicationUser newAdmin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            Name = "Administrator",
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow
        };

        IdentityResult createResult = await userManager.CreateAsync(newAdmin, adminPassword);

        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Kunne ikke opprette administrator: " +
                FormatIdentityErrors(createResult));
        }

        IdentityResult roleResult = await userManager.AddToRoleAsync(newAdmin, AdminRole);

        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(newAdmin);

            throw new InvalidOperationException(
                "Kunne ikke tildele Admin-rollen: " +
                FormatIdentityErrors(roleResult));
        }

        // Nye administratorer må bruke portalens e-postbaserte tofaktor.
        IdentityResult twoFactorResult = await userManager.SetTwoFactorEnabledAsync(newAdmin, true);

        if (!twoFactorResult.Succeeded)
        {
            await userManager.DeleteAsync(newAdmin);

            throw new InvalidOperationException(
                "Kunne ikke aktivere tofaktor for administrator: " +
                FormatIdentityErrors(twoFactorResult));
        }

        logger.LogInformation(
            "Administrator {UserId} er opprettet med tofaktorautentisering.",
            newAdmin.Id);
    }

    #endregion

    #region Hjelpemetoder

    /*
        Samler Identity-feilkoder uten å inkludere passord eller tokener.

        result: Resultatet fra en Identity-operasjon.
        Returnerer: En kommaseparert liste over feilkoder.
    */
    private static string FormatIdentityErrors(IdentityResult result)
    {
        return string.Join(", ", result.Errors.Select(error => error.Code));
    }

    #endregion
}
