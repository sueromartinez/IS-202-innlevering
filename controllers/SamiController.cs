using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Ai;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// En melding i chatten slik nettleseren sender den.
/// Bare tekst fra bruker og Sami; verktøykall skjer kun på serveren.
/// </summary>
public class SamiChatMessageDto
{
    public string Role { get; set; } = "user";

    public string Text { get; set; } = string.Empty;
}


public class SamiChatRequestDto
{
    public List<SamiChatMessageDto> Messages { get; set; } = new();
}


/// <summary>
/// Endepunktet chat-widgeten snakker med: POST /Sami/Chat.
///
/// - Krever innlogging og antiforgery-token (sendes i headeren
///   RequestVerificationToken fra widgeten).
/// - Rollen hentes fra Identity, aldri fra nettleseren.
/// - API-nøkkel og modell finnes bare på serveren.
/// </summary>
[Authorize]
public class SamiController : Controller
{
    private const int MaxMessages = 20;

    private const int MaxMessageLength = 2000;


    private readonly SamiAssistantService _sami;

    private readonly UserManager<ApplicationUser> _userManager;

    private readonly ILogger<SamiController> _logger;


    public SamiController(
        SamiAssistantService sami,
        UserManager<ApplicationUser> userManager,
        ILogger<SamiController> logger
    )
    {
        _sami = sami;
        _userManager = userManager;
        _logger = logger;
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Chat(
        [FromBody] SamiChatRequestDto request,
        CancellationToken cancellationToken
    )
    {
        // Bare de siste meldingene sendes til modellen,
        // slik at lange samtaler ikke blir trege eller dyre.
        List<SamiChatMessageDto> recent =
            (request.Messages ?? new List<SamiChatMessageDto>())
                .Where(message => !string.IsNullOrWhiteSpace(message.Text))
                .TakeLast(MaxMessages)
                .ToList();

        // Samtalen må starte med brukeren.
        while (recent.Count > 0 && recent[0].Role != "user")
        {
            recent.RemoveAt(0);
        }

        if (recent.Count == 0 || recent[^1].Role != "user")
        {
            return BadRequest(new { error = "Skriv en melding til Sami." });
        }

        List<ChatMessage> history =
            recent
                .Select(message => new ChatMessage
                {
                    Role = message.Role == "user" ? ChatRole.User : ChatRole.Assistant,
                    Text = message.Text.Length > MaxMessageLength
                        ? message.Text[..MaxMessageLength]
                        : message.Text
                })
                .ToList();

        ApplicationUser? user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Unauthorized();
        }

        var context = new SamiContext
        {
            UserId = user.Id,
            UserName = string.IsNullOrWhiteSpace(user.Name) ? "bruker" : user.Name,
            Role =
                User.IsInRole(IdentitySeeder.AdminRole) ? SamiRole.Admin
                : User.IsInRole(IdentitySeeder.PublicActorRole) ? SamiRole.PublicActor
                : SamiRole.Volunteer
        };

        try
        {
            SamiReply reply = await _sami.RunAsync(context, history, cancellationToken);

            return Json(new
            {
                reply = reply.Text,
                suggestions = reply.Suggestions.Select(suggestion => new
                {
                    needId = suggestion.NeedId,
                    resourceId = suggestion.ResourceId,
                    resourceName = suggestion.ResourceName,
                    resourceType = suggestion.ResourceTypeName,
                    needDescription = suggestion.NeedDescription,
                    distanceKm = suggestion.DistanceKm,
                    reason = suggestion.Reason
                }),
                // Utkast til ressurs. Feltnavnene er de samme som i registreringsskjemaet,
                // så widgeten kan sende dem rett til /Resource/RegisterResource.
                resourceDrafts = reply.ResourceDrafts.Select(draft => new
                {
                    fields = new Dictionary<string, string?>
                    {
                        ["ResourceType"] = draft.ResourceType,
                        ["ResourceName"] = draft.ResourceName,
                        ["Description"] = draft.Description,
                        ["Quantity"] = draft.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["Capacity"] = draft.Capacity,
                        ["AvailabilityMonths"] = draft.AvailabilityMonths.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["StreetAddress"] = draft.StreetAddress,
                        ["PostalLocation"] = draft.PostalLocation,
                        ["Municipality"] = draft.Municipality,
                        ["Latitude"] = draft.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["Longitude"] = draft.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["ContactPhoneCountryCode"] = draft.ContactPhoneCountryCode,
                        ["ContactPhoneNumber"] = draft.ContactPhoneNumber
                    },
                    summary = new
                    {
                        typeName = draft.ResourceTypeName,
                        name = draft.ResourceName,
                        description = draft.Description,
                        quantity = draft.Quantity,
                        capacity = draft.Capacity,
                        availability = draft.AvailabilityName,
                        address = string.Join(", ", new[] { draft.StreetAddress, draft.PostalLocation, draft.Municipality }
                            .Where(part => !string.IsNullOrWhiteSpace(part))),
                        phone = draft.ContactPhoneNumber is null
                            ? null
                            : draft.ContactPhoneCountryCode + " " + draft.ContactPhoneNumber
                    }
                })
            });
        }
        catch (ChatModelUnavailableException exception)
        {
            _logger.LogWarning(exception, "Sami er ikke tilgjengelig");

            return StatusCode(503, new
            {
                error = "Sami er ikke tilgjengelig akkurat nå. Du kan fortsatt bruke portalen som vanlig, "
                        + "f.eks. «Finn ressurser» på et behov."
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Brukeren lukket siden mens Sami tenkte.
            return new EmptyResult();
        }
        catch (Exception exception)
        {
            // Uventet feil (f.eks. et svar fra modellen vi ikke forstod).
            // Svarer med JSON, slik at widgeten kan vise en forståelig melding.
            _logger.LogError(exception, "Uventet feil i Sami");

            return StatusCode(500, new
            {
                error = "Noe gikk galt hos Sami. Prøv igjen, eller still spørsmålet på en annen måte."
            });
        }
    }
}
