using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.Services.Ai;
using FirstWebAppInDocker.Services.Reports;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

/// <summary>
/// Rapporter for offentlige aktører og administratorer:
/// - Situasjonsrapport med nøkkeltall, hastegrad og mangler,
///   og en kort oppsummering skrevet av Sami.
/// - Utskriftsvennlig ressursliste per kommune.
/// - Eksport av ressurslisten som CSV (Excel) eller JSON (andre systemer).
///
/// Frivillige har ikke tilgang, fordi listene viser alle ressurser
/// med kontaktinformasjon.
/// </summary>
[Authorize(Roles = IdentitySeeder.PublicActorRole + "," + IdentitySeeder.AdminRole)]
public class ReportController : Controller
{
    private readonly ReportService _reports;

    private readonly IChatModelClient _chatModel;

    private readonly ILogger<ReportController> _logger;


    public ReportController(
        ReportService reports,
        IChatModelClient chatModel,
        ILogger<ReportController> logger
    )
    {
        _reports = reports;
        _chatModel = chatModel;
        _logger = logger;
    }


    // =========================================================
    // SITUASJONSRAPPORT
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        SituationReportViewModel model = await _reports.BuildSituationAsync();

        return View(model);
    }


    /// <summary>
    /// Sami skriver en kort oppsummering av tallene.
    /// Kalles fra knappen på rapportsiden. Tallene regnes ut på nytt
    /// her, så modellen aldri får tall fra nettleseren.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        SituationReportViewModel report = await _reports.BuildSituationAsync();

        string facts = ReportService.BuildAiFacts(report);

        try
        {
            ChatModelResponse response =
                await _chatModel.CompleteAsync(
                    ReportService.AiSystemPrompt,
                    new[]
                    {
                        new ChatMessage
                        {
                            Role = ChatRole.User,
                            Text = "Lag situasjonsrapporten ut fra dette tallgrunnlaget:\n\n" + facts
                        }
                    },
                    Array.Empty<ChatToolDefinition>(),
                    cancellationToken);

            string text = (response.Text ?? string.Empty).Replace("**", "").Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                return StatusCode(503, new { error = "Sami ga ikke noe svar. Den automatiske oppsummeringen over gjelder fortsatt." });
            }

            await this.AuditAsync(AuditActions.ReportGenerated, "Situasjonsrapport med oppsummering fra Sami");

            return Json(new { text, generatedAt = report.GeneratedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") });
        }
        catch (ChatModelUnavailableException exception)
        {
            _logger.LogWarning(exception, "Sami var ikke tilgjengelig for situasjonsrapporten.");

            return StatusCode(503, new { error = "Sami er ikke tilgjengelig akkurat nå. Den automatiske oppsummeringen over gjelder fortsatt." });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Feil da Sami skulle lage situasjonsrapport.");

            return StatusCode(503, new { error = "Noe gikk galt da Sami skulle skrive oppsummeringen. Prøv igjen litt senere." });
        }
    }


    // =========================================================
    // RESSURSLISTE TIL UTSKRIFT
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> Resources(
        string? kommune,
        ResourceType? type,
        bool onlyAvailable = false
    )
    {
        List<ResourceReportRow> rows =
            await _reports.GetResourceRowsAsync(kommune, type, onlyAvailable);

        var model = new ResourceReportViewModel
        {
            Municipality = string.IsNullOrWhiteSpace(kommune) ? null : kommune.Trim(),
            Type = type,
            OnlyAvailable = onlyAvailable,
            Municipalities = await _reports.GetMunicipalitiesAsync(),
            GeneratedAt = DateTime.UtcNow,
            Groups = rows
                .GroupBy(row => row.Municipality)
                .Select(group => new ResourceReportGroup
                {
                    Municipality = group.Key,
                    Resources = group.ToList()
                })
                .ToList()
        };

        return View(model);
    }


    // =========================================================
    // EKSPORT
    // =========================================================

    /// <summary>
    /// GET /Report/Export?format=csv|json&amp;kommune=...&amp;type=...
    /// Samme filtre som utskriftssiden.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Export(
        string format,
        string? kommune,
        ResourceType? type,
        bool onlyAvailable = false
    )
    {
        bool json = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
        bool csv = string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

        if (!json && !csv)
        {
            return BadRequest("Ukjent format. Bruk csv eller json.");
        }

        List<ResourceReportRow> rows =
            await _reports.GetResourceRowsAsync(kommune, type, onlyAvailable);

        DateTime now = DateTime.UtcNow;

        string scope =
            string.IsNullOrWhiteSpace(kommune) ? "alle-kommuner" : Slug(kommune);

        string fileName = $"ressurser-{scope}-{now:yyyy-MM-dd}.{(json ? "json" : "csv")}";

        string filterText =
            $"{rows.Count} ressurser som {(json ? "JSON" : "CSV")}"
            + (string.IsNullOrWhiteSpace(kommune) ? "" : $", kommune {kommune.Trim()}")
            + (type.HasValue ? $", type {EnumDisplay.Name(type.Value)}" : "")
            + (onlyAvailable ? ", bare tilgjengelige" : "");

        await this.AuditAsync(AuditActions.Export, filterText);

        return json
            ? File(ReportService.ToJson(rows, now), "application/json; charset=utf-8", fileName)
            : File(ReportService.ToCsv(rows), "text/csv; charset=utf-8", fileName);
    }


    /// <summary>
    /// «Kristiansand S» → «kristiansand-s», trygt i filnavn.
    /// </summary>
    public static string Slug(string value)
    {
        string lower = value.Trim().ToLowerInvariant()
            .Replace("æ", "ae").Replace("ø", "o").Replace("å", "a");

        var chars = lower.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();

        string slug = new string(chars);

        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        slug = slug.Trim('-');

        return slug.Length == 0 ? "kommune" : slug;
    }
}
