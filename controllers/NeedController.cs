using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Registrering, oversikt, endring og sletting av behov.
/// Kun for offentlige aktører.
/// </summary>
[Authorize(
    Roles =
        IdentitySeeder.PublicActorRole
        + ","
        + IdentitySeeder.AdminRole
)]
public class NeedController : Controller
{
    private readonly ApplicationDbContext _context;

    public NeedController(ApplicationDbContext context)
    {
        _context = context;
    }


    // =========================
    // OVERSIKT
    // =========================

    /// <summary>
    /// Viser alle registrerte behov, nyeste først.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> NeedOverview()
    {
        if (!IsPublicActor())
        {
            return RedirectToAction("Dashboard", "Home");
        }

        List<Need> needs = await _context.Needs
            .AsNoTracking()
            .OrderByDescending(need => need.CreatedAt)
            .ToListAsync();

        return View(needs);
    }


    // =========================
    // REGISTRER
    // =========================

    [HttpGet]
    public IActionResult RegisterNeed()
    {
        if (!IsPublicActor())
        {
            return RedirectToAction("Dashboard", "Home");
        }

        // Skjemaet starter med ett tomt behov
        var model = new RegisterNeedsViewModel();
        model.Needs.Add(new NeedItemViewModel());

        return View(model);
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegisterNeed(RegisterNeedsViewModel model)
    {
        if (!IsPublicActor())
        {
            return RedirectToAction("Dashboard", "Home");
        }

        if (model.Needs.Count == 0)
        {
            model.Needs.Add(new NeedItemViewModel());
            ModelState.AddModelError(string.Empty, "Legg til minst ett behov.");
            return View(model);
        }

        // Hvert behov må ha en plassering
        for (int i = 0; i < model.Needs.Count; i++)
        {
            if (model.Needs[i].Latitude is null || model.Needs[i].Longitude is null)
            {
                ModelState.AddModelError($"Needs[{i}].Latitude", "Velg plassering i kartet.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        string ownerEmail = HttpContext.Session.GetString("UserEmail")!;

        foreach (var item in model.Needs)
        {
            _context.Needs.Add(new Need
            {
                ResourceType = item.ResourceType!.Value,
                Priority = item.Priority,
                Quantity = item.Quantity,
                Description = item.Description.Trim(),
                Latitude = item.Latitude!.Value,
                Longitude = item.Longitude!.Value,
                OwnerEmail = ownerEmail
            });
        }

        await _context.SaveChangesAsync();

        TempData["NeedMessage"] = $"{model.Needs.Count} behov ble registrert.";

        return RedirectToAction(nameof(NeedOverview));
    }


    // =========================
    // ENDRE
    // =========================

    /// <summary>
    /// Viser skjemaet for å endre ett behov.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> EditNeed(int id)
    {
        if (!IsPublicActor())
        {
            return RedirectToAction("Dashboard", "Home");
        }

        Need? need = await FindOwnNeed(id);

        if (need == null)
        {
            return NotFound();
        }

        var model = new NeedItemViewModel
        {
            Id = need.Id,
            ResourceType = need.ResourceType,
            Priority = need.Priority,
            Quantity = need.Quantity,
            Description = need.Description,
            Latitude = need.Latitude,
            Longitude = need.Longitude
        };

        return View(model);
    }


    /// <summary>
    /// Lagrer endringene på et behov.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditNeed(NeedItemViewModel model)
    {
        if (!IsPublicActor())
        {
            return RedirectToAction("Dashboard", "Home");
        }

        Need? need = await FindOwnNeed(model.Id);

        if (need == null)
        {
            return NotFound();
        }

        if (model.Latitude is null || model.Longitude is null)
        {
            ModelState.AddModelError(nameof(model.Latitude), "Velg plassering i kartet.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        need.ResourceType = model.ResourceType!.Value;
        need.Priority = model.Priority;
        need.Quantity = model.Quantity;
        need.Description = model.Description.Trim();
        need.Latitude = model.Latitude!.Value;
        need.Longitude = model.Longitude!.Value;

        await _context.SaveChangesAsync();

        TempData["NeedMessage"] = "Behovet ble oppdatert.";

        return RedirectToAction(nameof(NeedOverview));
    }


    // =========================
    // SLETT
    // =========================

    /// <summary>
    /// Sletter ett behov. Kun POST, slik at en lenke
    /// alene aldri kan slette noe.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNeed(int id)
    {
        if (!IsPublicActor())
        {
            return RedirectToAction("Dashboard", "Home");
        }

        Need? need = await FindOwnNeed(id);

        if (need == null)
        {
            return NotFound();
        }

        _context.Needs.Remove(need);

        await _context.SaveChangesAsync();

        TempData["NeedMessage"] = "Behovet ble slettet.";

        return RedirectToAction(nameof(NeedOverview));
    }


    // =========================
    // HJELPEMETODER
    // =========================

    /// <summary>
    /// Henter et behov, men bare hvis innlogget bruker
    /// selv har registrert det. Ellers null.
    /// </summary>
    private async Task<Need?> FindOwnNeed(int id)
    {
        string? userEmail = HttpContext.Session.GetString("UserEmail");

        return await _context.Needs.FirstOrDefaultAsync(
            need => need.Id == id && need.OwnerEmail == userEmail
        );
    }


    /// <summary>
    /// Midlertidig Session-sjekk frem til Identity innføres.
    /// </summary>
    private bool IsPublicActor()
    {
        return HttpContext.Session.GetString("UserRole") == "PublicActor";
    }
}