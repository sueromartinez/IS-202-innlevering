/*
    ReportController.cs – situasjonsrapport og eksport

    Hva filen gjør: Lager situasjonsrapporten, utskriftsvennlig ressursliste og eksport til
    CSV/JSON. Kan også be Sami skrive en kort oppsummering.

    På nettsiden: Siden «Rapporter» med nøkkeltall, knappen for Sami-oppsummering, ressurslisten
    per kommune og knappene «Last ned CSV/JSON».

    Oppgavekrav: 10, 19, 20
*/

using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.Services.Ai;
using FirstWebAppInDocker.Services.Reports;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Controllers;

/*
    Rapporter for offentlige aktører og administratorer:
    - Situasjonsrapport med nøkkeltall og mangler,
      og en kort oppsummering skrevet av Sami.
    - Utskriftsvennlig ressursliste per kommune.
    - Eksport av ressurslisten som CSV (Excel) eller JSON (andre systemer).

    Frivillige har ikke tilgang, fordi listene viser alle ressurser
    med kontaktinformasjon.
*/
[Authorize(Roles = IdentitySeeder.PublicActorRole + "," + IdentitySeeder.AdminRole)]
public class ReportController : Controller
{
    #region Felt og konstruktør

    private readonly ReportService _reports;

    private readonly IChatModelClient _chatModel;

    private readonly ILogger<ReportController> _logger;

    public ReportController(
        ReportService reports,
        IChatModelClient chatModel,
        ILogger<ReportController> logger)
    {
        _reports = reports;
        _chatModel = chatModel;
        _logger = logger;
    }

    #endregion

    #region Situasjonsrapport

    // GET /Report
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        SituationReportViewModel model = await _reports.BuildSituationAsync();

        return View(model);
    }

    /*
        POST /Report/Summary
        Sami skriver oppsummeringen. Sami får bare ferdige tall, ikke tilgang til
        databasen. Da kan ikke Sami finne på tall, og vi sender minst mulig data til en ekstern
        tjeneste.
    */
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

    #endregion

    #region Ressursliste og eksport

    /*
        GET /Report/Resources
        Ressurslisten, filtrert på kommune og type, laget for utskrift.
    */
    [HttpGet]
    public async Task<IActionResult> Resources(
        string? kommune,
        ResourceType? type,
        bool onlyAvailable = false)
    {
        List<ResourceReportRow> rows =
            await _reports.GetResourceRowsAsync(kommune, type, onlyAvailable);

        ResourceReportViewModel model = new ResourceReportViewModel
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

    /*
        GET /Report/Export
        Laster ned en fil (CSV kan åpnes i Excel). Eksporten skrives i aktivitetsloggen.
    */
    [HttpGet]
    public async Task<IActionResult> Export(
        string format,
        string? kommune,
        ResourceType? type,
        bool onlyAvailable = false)
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

        string scope = string.IsNullOrWhiteSpace(kommune) ? "alle-kommuner" : Slug(kommune);

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

    #endregion

    #region Hjelpemetoder

    // Lager et trygt filnavn, der æ, ø og å byttes ut.
    public static string Slug(string value)
    {
        string lower = value.Trim().ToLowerInvariant()
            .Replace("æ", "ae").Replace("ø", "o").Replace("å", "a");

        char[] chars = lower.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();

        string slug = new string(chars);

        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        slug = slug.Trim('-');

        return slug.Length == 0 ? "kommune" : slug;
    }

    #endregion
}
