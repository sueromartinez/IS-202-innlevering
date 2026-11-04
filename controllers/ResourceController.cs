using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Håndterer registrering og visning av ressurser.
///
/// Tilgang styres med ASP.NET Core Identity-roller.
/// </summary>
[Authorize(
    Roles =
        IdentitySeeder.ResourceProviderRole
        + ","
        + IdentitySeeder.PublicActorRole
        + ","
        + IdentitySeeder.AdminRole
)]
public class ResourceController : Controller
{
    private readonly ApplicationDbContext
        _dbContext;

    private readonly UserManager<ApplicationUser>
        _userManager;


    public ResourceController(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager
    )
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }


    // =========================
    // REGISTER RESOURCE - GET
    // =========================

    [HttpGet]
    [Authorize(
        Roles =
            IdentitySeeder.ResourceProviderRole
    )]
    public IActionResult RegisterResource()
    {
        return View(
            new RegisterResourceViewModel()
        );
    }


    // =========================
    // REGISTER RESOURCE - POST
    // =========================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(
        Roles =
            IdentitySeeder.ResourceProviderRole
    )]
    public async Task<IActionResult>
        RegisterResource(
            RegisterResourceViewModel model
        )
    {
        if (!ModelState.IsValid)
        {
            return View(
                model
            );
        }


        if (!model.ResourceType.HasValue)
        {
            ModelState.AddModelError(
                nameof(model.ResourceType),
                "Velg type ressurs."
            );

            return View(
                model
            );
        }


        ApplicationUser? user =
            await _userManager
                .GetUserAsync(
                    User
                );


        if (
            user is null
            ||
            string.IsNullOrWhiteSpace(
                user.Email
            )
        )
        {
            return Challenge();
        }

        // Takes ownership from the server session, never from posted form data.
        var resource = new Resource
        {
            ResourceType =
                model.ResourceType!.Value,

            
        
            ResourceName =
                model.ResourceName.Trim(), //endret l

            Quantity =  
                model.Quantity, //endret l

            Capacity =  
                NormalizeOptionalText(
                    model.Capacity
                ), // endret l

            Availability =  
                NormalizeOptionalText(
                    model.Availability
                ), // endret l
            
            Address =  
                NormalizeOptionalText(
                    model.Address), // endret l

            Description =
                model.Description.Trim(),

            
    
    
            Latitude =
                model.Latitude,

            Longitude =
                model.Longitude,

            ContactEmail =
                NormalizeOptionalText(
                    model.ContactEmail
                ),

            ContactPhoneCountryCode =
                string.IsNullOrWhiteSpace(
                    model.ContactPhoneNumber
                )
                    ? null
                    : model.ContactPhoneCountryCode,

            ContactPhoneNumber =
                NormalizePhoneNumber(
                    model.ContactPhoneNumber
                ),

            OwnerEmail =
                userEmail,

            CreatedAt =
                DateTime.UtcNow
        };

        // Saves all validated fields through the existing MariaDB context.
        _context.Resources.Add(resource);

        await _context.SaveChangesAsync();

        var resource =
            new Resource
            {
                // =========================
                // RESOURCE INFORMATION
                // =========================

                ResourceType =
                    model.ResourceType.Value,

                ResourceName =
                    model.ResourceName.Trim(),

                Description =
                    model.Description.Trim(),

                Quantity =
                    model.Quantity,

        // Restricts provider results in SQL before sending data to the browser.
        IQueryable<Resource> query =
            _context.Resources
                .AsNoTracking();
                Capacity =
                    string.IsNullOrWhiteSpace(
                        model.Capacity
                    )
                        ? null
                        : model.Capacity.Trim(),

                Availability =
                    string.IsNullOrWhiteSpace(
                        model.Availability
                    )
                        ? null
                        : model.Availability.Trim(),


                // =========================
                // LOCATION
                // =========================

                Address =
                    string.IsNullOrWhiteSpace(
                        model.Address
                    )
                        ? null
                        : model.Address.Trim(),

                Latitude =
                    model.Latitude,

                Longitude =
                    model.Longitude,


                // =========================
                // CONTACT
                // =========================

                ContactEmail =
                    string.IsNullOrWhiteSpace(
                        model.ContactEmail
                    )
                        ? user.Email
                        : model.ContactEmail.Trim(),

                ContactPhoneCountryCode =
                    string.IsNullOrWhiteSpace(
                        model.ContactPhoneCountryCode
                    )
                        ? null
                        : model
                            .ContactPhoneCountryCode
                            .Trim(),

                ContactPhoneNumber =
                    string.IsNullOrWhiteSpace(
                        model.ContactPhoneNumber
                    )
                        ? null
                        : model
                            .ContactPhoneNumber
                            .Trim(),


                // =========================
                // OWNERSHIP
                // =========================

                OwnerUserId =
                    user.Id,

                OwnerEmail =
                    user.Email,


                // =========================
                // METADATA
                // =========================

                CreatedAt =
                    DateTime.UtcNow
            };


        _dbContext.Resources.Add(
            resource
        );


        await _dbContext
            .SaveChangesAsync();


        TempData["ResourceSuccess"] =
            "Ressursen ble registrert.";


        return RedirectToAction(
            nameof(ResourceOverview)
        );
    }


    // =========================
    // RESOURCE OVERVIEW
    // =========================

    [HttpGet]
    public async Task<IActionResult>
        ResourceOverview()
    {
        ApplicationUser? user =
            await _userManager
                .GetUserAsync(
                    User
                );


        if (
            user is null
            ||
            string.IsNullOrWhiteSpace(
                user.Email
            )
        )
        {
            return Challenge();
        }


        bool isPublicActor =
            User.IsInRole(
                IdentitySeeder.PublicActorRole
            );


        bool isAdmin =
            User.IsInRole(
                IdentitySeeder.AdminRole
            );


        bool isResourceProvider =
            User.IsInRole(
                IdentitySeeder.ResourceProviderRole
            );


        IQueryable<Resource> query =
            _dbContext.Resources
                .AsNoTracking();


        // =========================
        // RESOURCE PROVIDER FILTER
        // =========================
        //
        // Nye ressurser kobles til permanent
        // Identity UserId.
        //
        // Gamle ressurser kan fortsatt ha
        // OwnerUserId = null og matches da
        // midlertidig på e-post.
        //

        if (
            isResourceProvider
            &&
            !isPublicActor
            &&
            !isAdmin
        )
        {
            query =
                query.Where(
                    resource =>
                        resource.OwnerUserId
                            == user.Id
                        ||
                        (
                            resource.OwnerUserId
                                == null
                            &&
                            resource.OwnerEmail
                                == user.Email
                        )
                );
        }


        List<ResourceOverviewViewModel>
            resources =
                await query
                    .OrderByDescending(
                        resource =>
                            resource.CreatedAt
                    )
                    .Select(
                        resource =>
                            new ResourceOverviewViewModel
                            {
                                Id =
                                    resource.Id,

                                ResourceType =
                                    resource.ResourceType,

                                Description =
                                    resource.Description,

                                Position =
                                    new Coordinate(
                                        resource.Latitude,
                                        resource.Longitude
                                    ),

                                ContactEmail =
                                    resource.ContactEmail,

                                ContactPhoneCountryCode =
                                    resource
                                        .ContactPhoneCountryCode,

                                ContactPhoneNumber =
                                    resource
                                        .ContactPhoneNumber,

                                OwnerEmail =
                                    resource.OwnerEmail,

                                CreatedAt =
                                    resource.CreatedAt
                            }
                    )
                    .ToListAsync();


        if (isAdmin)
        {
            ViewBag.UserRole =
                IdentitySeeder.AdminRole;
        }
        else if (isPublicActor)
        {
            ViewBag.UserRole =
                IdentitySeeder.PublicActorRole;
        }
        else
        {
            ViewBag.UserRole =
                IdentitySeeder.ResourceProviderRole;
        }


        return View(
            resources
        );
    }
}