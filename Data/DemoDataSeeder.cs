using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Data;

/// <summary>
/// Fyller en tom database med falske data for demo (EXPO):
/// to ressursleverandører med ressurser rundt Grimstad, Arendal,
/// Lillesand og Kristiansand, og noen behov fra den offentlige testbrukeren.
///
/// Gjør ingenting hvis det allerede finnes ressurser eller behov.
/// Alle personer, firma og telefonnummer er oppdiktet.
/// </summary>
public static class DemoDataSeeder
{
    public const string VolunteerEmail = "frivillig@beredskap.no";

    public const string CompanyEmail = "entreprenor@beredskap.no";

    private const string DemoPassword = "Test123!";


    public static async Task SeedAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await context.Resources.AnyAsync() || await context.Needs.AnyAsync())
        {
            return;
        }

        // Behovene eies av den offentlige testbrukeren fra IdentitySeeder.
        ApplicationUser? publicActor =
            await userManager.FindByEmailAsync(IdentitySeeder.PublicActorEmail);

        ApplicationUser volunteer =
            await EnsureUserAsync(userManager, VolunteerEmail, "Kari Frivillig");

        ApplicationUser company =
            await EnsureUserAsync(userManager, CompanyEmail, "Sørlandet Maskin AS (demo)");


        context.Resources.AddRange(
            CreateResource(volunteer, ResourceType.Boat, "Sjark med påhengsmotor",
                "5,5 m båt, kan frakte personer og utstyr i grunt vann.", 1, "6 personer",
                "Hele døgnet ved varsel", "Grimstad gjestehavn", 58.3399, 8.5950, "90000001"),

            CreateResource(volunteer, ResourceType.Generator, "Bensinaggregat 3 kW",
                "Bærbart aggregat, nok til lys, varme og lading.", 2, "3 kW",
                "Kvelder og helg", "Fevik", 58.3797, 8.6886, "90000001"),

            CreateResource(volunteer, ResourceType.Personnel, "Frivillige fra idrettslaget",
                "Gruppe som kan bære, fylle sandsekker og hjelpe med evakuering.", 8, "8 personer",
                "Etter avtale", "Grimstad", 58.3436, 8.5838, "90000001"),

            CreateResource(company, ResourceType.Machinery, "Hjullaster",
                "Hjullaster med skuffe, kan flytte masser og rydde veier.", 1, "2,5 m³ skuffe",
                "Hverdager 07–16, ellers ved behov", "Arendal industriområde", 58.4570, 8.7500, "90000002"),

            CreateResource(company, ResourceType.SandAndGravel, "Sand og sandsekker",
                "Tørr sand og 500 tomme sandsekker på lager.", 500, "20 tonn sand",
                "Hele døgnet", "Lillesand", 58.2500, 8.3780, "90000002"),

            CreateResource(company, ResourceType.Transport, "Lastebil med kran",
                "Lastebil som kan frakte aggregat, sand og maskiner.", 1, "12 tonn",
                "Hverdager", "Kristiansand øst", 58.1530, 8.0400, "90000002"),

            CreateResource(company, ResourceType.Drone, "Drone med varmekamera",
                "Sertifisert operatør med drone for søk og observasjon.", 1, "40 min flytid",
                "Ved varsel", "Arendal", 58.4615, 8.7725, "90000002"),

            CreateResource(volunteer, ResourceType.Premises, "Grendehus med kjøkken",
                "Oppvarmet lokale som kan brukes til evakuerte.", 1, "60 personer",
                "Hele døgnet", "Landvik", 58.3570, 8.5170, "90000001")
        );


        context.Needs.AddRange(
            CreateNeed(publicActor?.Id, ResourceType.SandAndGravel, NeedPriority.Acute, 200,
                "Flom i Grimstad sentrum. Trenger sandsekker for å beskytte bygg ved havna.", 58.3405, 8.5934),

            CreateNeed(publicActor?.Id, ResourceType.Boat, NeedPriority.Acute, 2,
                "Evakuering av beboere i lavtliggende områder i Grimstad sentrum.", 58.3418, 8.5905),

            CreateNeed(publicActor?.Id, ResourceType.Generator, NeedPriority.Planned, 3,
                "Reservestrøm til sykehjem i Arendal ved varslet uvær.", 58.4630, 8.7660),

            CreateNeed(publicActor?.Id, ResourceType.Drone, NeedPriority.Planned, 1,
                "Oversiktsbilder av flomområdet langs Nidelva.", 58.4300, 8.7100)
        );

        await context.SaveChangesAsync();
    }


    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string name
    )
    {
        ApplicationUser? user = await userManager.FindByEmailAsync(email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                Name = name,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow
            };

            IdentityResult result = await userManager.CreateAsync(user, DemoPassword);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Kunne ikke opprette demobruker {email}.");
            }
        }

        if (!await userManager.IsInRoleAsync(user, IdentitySeeder.ResourceProviderRole))
        {
            await userManager.AddToRoleAsync(user, IdentitySeeder.ResourceProviderRole);
        }

        return user;
    }


    private static Resource CreateResource(
        ApplicationUser owner,
        ResourceType type,
        string name,
        string description,
        int quantity,
        string capacity,
        string availability,
        string address,
        double latitude,
        double longitude,
        string phone
    )
    {
        return new Resource
        {
            ResourceType = type,
            ResourceName = name,
            Description = description,
            Quantity = quantity,
            Capacity = capacity,
            Availability = availability,
            Address = address,
            Latitude = latitude,
            Longitude = longitude,
            ContactEmail = owner.Email,
            ContactPhoneCountryCode = "+47",
            ContactPhoneNumber = phone,
            OwnerUserId = owner.Id,
            OwnerEmail = owner.Email!,
            Status = ResourceStatus.Available,
            CreatedAt = DateTime.UtcNow
        };
    }


    private static Need CreateNeed(
        string? ownerUserId,
        ResourceType type,
        NeedPriority priority,
        int quantity,
        string description,
        double latitude,
        double longitude
    )
    {
        return new Need
        {
            ResourceType = type,
            Priority = priority,
            Quantity = quantity,
            Description = description,
            Latitude = latitude,
            Longitude = longitude,
            // Den offentlige testbrukeren eier behovene.
            OwnerUserId = ownerUserId,
            OwnerEmail = IdentitySeeder.PublicActorEmail,
            Status = NeedStatus.New
        };
    }
}
