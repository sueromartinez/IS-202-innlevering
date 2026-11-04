using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Identity;

namespace FirstWebAppInDocker.Data;

/// <summary>
/// Oppretter roller og nødvendige testbrukere
/// for Beredskapsportalen.
/// </summary>
public static class IdentitySeeder
{
    public const string AdminRole =
        "Admin";

    public const string PublicActorRole =
        "PublicActor";

    public const string ResourceProviderRole =
        "ResourceProvider";


    // Midlertidig testbruker.
    // Denne skal senere flyttes til konfigurasjon/.env
    // eller erstattes av administrert brukeropprettelse.
    public const string PublicActorEmail =
        "offentlig@beredskap.no";

    public const string PublicActorPassword =
        "Test123!";


    public static async Task SeedAsync(
        IServiceProvider services
    )
    {
        using IServiceScope scope =
            services.CreateScope();


        RoleManager<IdentityRole> roleManager =
            scope.ServiceProvider
                .GetRequiredService<
                    RoleManager<IdentityRole>
                >();


        UserManager<ApplicationUser> userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>
                >();


        // =========================
        // ROLES
        // =========================

        string[] roles =
        {
            AdminRole,
            PublicActorRole,
            ResourceProviderRole
        };


        foreach (string roleName in roles)
        {
            bool roleExists =
                await roleManager
                    .RoleExistsAsync(
                        roleName
                    );


            if (roleExists)
            {
                continue;
            }


            IdentityResult result =
                await roleManager.CreateAsync(
                    new IdentityRole(
                        roleName
                    )
                );


            if (!result.Succeeded)
            {
                string errors =
                    string.Join(
                        ", ",
                        result.Errors.Select(
                            error =>
                                error.Description
                        )
                    );


                throw new InvalidOperationException(
                    $"Kunne ikke opprette rollen '{roleName}': {errors}"
                );
            }
        }


        // =========================
        // PUBLIC ACTOR TEST USER
        // =========================

        ApplicationUser? publicActor =
            await userManager.FindByEmailAsync(
                PublicActorEmail
            );


        if (publicActor is null)
        {
            publicActor =
                new ApplicationUser
                {
                    UserName =
                        PublicActorEmail,

                    Email =
                        PublicActorEmail,

                    Name =
                        "Beredskapsbruker",

                    EmailConfirmed =
                        true,

                    CreatedAt =
                        DateTime.UtcNow
                };


            IdentityResult createResult =
                await userManager.CreateAsync(
                    publicActor,
                    PublicActorPassword
                );


            if (!createResult.Succeeded)
            {
                string errors =
                    string.Join(
                        ", ",
                        createResult.Errors.Select(
                            error =>
                                error.Description
                        )
                    );


                throw new InvalidOperationException(
                    $"Kunne ikke opprette offentlig testbruker: {errors}"
                );
            }
        }


        bool hasPublicActorRole =
            await userManager.IsInRoleAsync(
                publicActor,
                PublicActorRole
            );


        if (!hasPublicActorRole)
        {
            IdentityResult roleResult =
                await userManager.AddToRoleAsync(
                    publicActor,
                    PublicActorRole
                );


            if (!roleResult.Succeeded)
            {
                string errors =
                    string.Join(
                        ", ",
                        roleResult.Errors.Select(
                            error =>
                                error.Description
                        )
                    );


                throw new InvalidOperationException(
                    $"Kunne ikke gi testbrukeren rollen '{PublicActorRole}': {errors}"
                );
            }
        }
    }
}