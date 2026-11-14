using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Matching;
using FirstWebAppInDocker.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Services.Reports;

/// <summary>
/// Henter data til utskrift, eksport og situasjonsrapport.
///
/// All utregning skjer her og ikke i språkmodellen, slik at tallene
/// alltid stemmer og rapporten virker selv uten AI.
/// Hjelpemetodene som ikke trenger database er statiske og testbare.
/// </summary>
public class ReportService
{
    private readonly ApplicationDbContext _context;


    public ReportService(ApplicationDbContext context)
    {
        _context = context;
    }


    // =========================================================
    // RESSURSLISTE (utskrift og eksport)
    // =========================================================

    /// <summary>
    /// Alle ressurser, eventuelt filtrert på kommune, type og tilgjengelighet.
    /// Sortert på kommune og deretter navn.
    /// </summary>
    public async Task<List<ResourceReportRow>> GetResourceRowsAsync(
        string? municipality,
        ResourceType? type,
        bool onlyAvailable
    )
    {
        DateTime now = DateTime.UtcNow;

        IQueryable<Resource> query =
            _context.Resources
                .AsNoTracking()
                .Include(resource => resource.Owner);

        if (type.HasValue)
        {
            query = query.Where(resource => resource.ResourceType == type.Value);
        }

        List<Resource> resources = await query.ToListAsync();

        IEnumerable<ResourceReportRow> rows =
            resources.Select(resource => ToRow(resource, now));

        if (!string.IsNullOrWhiteSpace(municipality))
        {
            rows = rows.Where(row =>
                string.Equals(row.Municipality, municipality.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (onlyAvailable)
        {
            rows = rows.Where(row => row.Status == ResourceStatus.Available);
        }

        return rows
            .OrderBy(row => row.Municipality, StringComparer.Create(CultureInfo.InvariantCulture, true))
            .ThenBy(row => row.Name, StringComparer.Create(CultureInfo.InvariantCulture, true))
            .ToList();
    }


    /// <summary>
    /// Alle kommuner som har minst én ressurs (til nedtrekkslisten).
    /// </summary>
    public async Task<List<string>> GetMunicipalitiesAsync()
    {
        List<string?> addresses =
            await _context.Resources
                .AsNoTracking()
                .Select(resource => resource.Address)
                .ToListAsync();

        return addresses
            .Select(ResourceDisplay.Municipality)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Create(CultureInfo.InvariantCulture, true))
            .ToList();
    }


    public static ResourceReportRow ToRow(Resource resource, DateTime nowUtc)
    {
        ResourceStatus status =
            ResourceAvailability.EffectiveStatus(resource.Status, resource.Availability, nowUtc);

        string? phone =
            string.IsNullOrWhiteSpace(resource.ContactPhoneNumber)
                ? resource.Owner?.PhoneNumber
                : $"{resource.ContactPhoneCountryCode} {resource.ContactPhoneNumber}".Trim();

        return new ResourceReportRow
        {
            Id = resource.Id,
            Name = resource.ResourceName,
            Type = resource.ResourceType,
            TypeName = EnumDisplay.Name(resource.ResourceType),
            Quantity = resource.Quantity,
            Capacity = resource.Capacity,
            Description = resource.Description,
            Municipality = ResourceDisplay.Municipality(resource.Address),
            Address = ResourceDisplay.PrettyAddress(resource.Address),
            Status = status,
            StatusName = EnumDisplay.Name(status),
            AvailableUntil = ResourceAvailability.GetEndDate(resource.Availability),
            OwnerName = resource.Owner?.Name,
            ContactEmail = string.IsNullOrWhiteSpace(resource.ContactEmail) ? resource.OwnerEmail : resource.ContactEmail,
            ContactPhone = phone,
            Latitude = resource.Latitude,
            Longitude = resource.Longitude,
            RegisteredAt = resource.CreatedAt
        };
    }


    /// <summary>
    /// CSV med semikolon og UTF-8 BOM, slik at norsk Excel
    /// åpner filen med riktige kolonner og æøå.
    /// </summary>
    public static byte[] ToCsv(IEnumerable<ResourceReportRow> rows)
    {
        var builder = new StringBuilder();

        builder.AppendLine(string.Join(';', new[]
        {
            "Id", "Navn", "Type", "Antall", "Kapasitet", "Status", "Tilgjengelig til",
            "Kommune", "Adresse", "Breddegrad", "Lengdegrad",
            "Eier", "E-post", "Telefon", "Registrert", "Beskrivelse"
        }));

        foreach (ResourceReportRow row in rows)
        {
            builder.AppendLine(string.Join(';', new[]
            {
                row.Id.ToString(CultureInfo.InvariantCulture),
                Csv(row.Name),
                Csv(row.TypeName),
                row.Quantity.ToString(CultureInfo.InvariantCulture),
                Csv(row.Capacity),
                Csv(row.StatusName),
                row.AvailableUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                Csv(row.Municipality),
                Csv(row.Address),
                row.Latitude.ToString("0.000000", CultureInfo.InvariantCulture),
                row.Longitude.ToString("0.000000", CultureInfo.InvariantCulture),
                Csv(row.OwnerName),
                Csv(row.ContactEmail),
                Csv(row.ContactPhone),
                row.RegisteredAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                Csv(row.Description)
            }));
        }

        byte[] bom = Encoding.UTF8.GetPreamble();
        byte[] content = Encoding.UTF8.GetBytes(builder.ToString());

        return bom.Concat(content).ToArray();
    }


    /// <summary>
    /// Setter felt i anførselstegn når de inneholder skilletegn,
    /// og stopper formel-injeksjon i Excel (=, +, -, @).
    /// </summary>
    public static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        string text = value.Replace("\r", " ").Replace("\n", " ");

        if (text[0] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }

        bool needsQuotes = text.IndexOfAny(new[] { ';', '"', ',' }) >= 0;

        return needsQuotes
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }


    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Skriver æøå som vanlige tegn i stedet for æ.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };


    /// <summary>
    /// JSON for andre systemer (f.eks. Heimevernets egne verktøy).
    /// Enum-verdiene skrives som tekst, ikke tall.
    /// </summary>
    public static byte[] ToJson(IEnumerable<ResourceReportRow> rows, DateTime generatedAtUtc)
    {
        var document = new
        {
            generatedAt = generatedAtUtc.ToString("o", CultureInfo.InvariantCulture),
            source = "Beredskapsportalen",
            count = rows.Count(),
            resources = rows.Select(row => new
            {
                row.Id,
                row.Name,
                Type = row.Type.ToString(),
                row.TypeName,
                row.Quantity,
                row.Capacity,
                Status = row.Status.ToString(),
                row.StatusName,
                AvailableUntil = row.AvailableUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.Municipality,
                row.Address,
                row.Latitude,
                row.Longitude,
                row.OwnerName,
                row.ContactEmail,
                row.ContactPhone,
                row.RegisteredAt,
                row.Description
            })
        };

        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }


    // =========================================================
    // SITUASJONSRAPPORT
    // =========================================================

    public async Task<SituationReportViewModel> BuildSituationAsync()
    {
        DateTime now = DateTime.UtcNow;

        List<Need> needs =
            await _context.Needs
                .AsNoTracking()
                .ToListAsync();

        List<Need> openNeeds =
            needs.Where(need => need.Status != NeedStatus.Resolved).ToList();

        Dictionary<int, NeedUrgency> urgency =
            await new NeedUrgencyService(_context).CalculateAsync(openNeeds);

        List<Resource> resources =
            await _context.Resources
                .AsNoTracking()
                .ToListAsync();

        var resourceStatuses =
            resources
                .Select(resource => new
                {
                    resource.ResourceType,
                    resource.Address,
                    Status = ResourceAvailability.EffectiveStatus(resource.Status, resource.Availability, now)
                })
                .ToList();

        DateTime dayAgo = now.AddHours(-24);

        int pendingRequests =
            await _context.NeedAssignments
                .CountAsync(assignment => assignment.Status == AssignmentStatus.Requested);

        int confirmedLast24h =
            await _context.NeedAssignments
                .CountAsync(assignment =>
                    assignment.Status == AssignmentStatus.Confirmed
                    && assignment.RespondedAt != null
                    && assignment.RespondedAt >= dayAgo);

        // Åpne behov som ikke er tildelt ennå: disse er det som faktisk mangler.
        List<Need> unassigned =
            openNeeds
                .Where(need => need.Status is NeedStatus.New or NeedStatus.UnderReview)
                .ToList();

        List<TypeCoverageRow> coverage =
            Enum.GetValues<ResourceType>()
                .Select(type => new TypeCoverageRow
                {
                    Type = type,
                    TypeName = EnumDisplay.Name(type),
                    OpenNeeds = unassigned.Count(need => need.ResourceType == type),
                    AvailableResources = resourceStatuses.Count(resource =>
                        resource.ResourceType == type && resource.Status == ResourceStatus.Available)
                })
                .Where(row => row.OpenNeeds > 0 || row.AvailableResources > 0)
                .OrderByDescending(row => row.IsGap)
                .ThenByDescending(row => row.IsTight)
                .ThenByDescending(row => row.OpenNeeds)
                .ToList();

        List<UrgentNeedRow> urgentRows =
            openNeeds
                .Select(need => new UrgentNeedRow
                {
                    Id = need.Id,
                    Description = need.Description,
                    TypeName = EnumDisplay.Name(need.ResourceType),
                    PriorityName = EnumDisplay.Name(need.Priority),
                    StatusName = EnumDisplay.Name(need.Status),
                    IsAssigned = need.Status == NeedStatus.Assigned,
                    Quantity = need.Quantity,
                    Deadline = need.Deadline,
                    Urgency = urgency.GetValueOrDefault(need.Id)
                              ?? NeedUrgencyService.Calculate(need, 0, now)
                })
                .OrderByDescending(row => row.Urgency.Score)
                .ToList();

        var model = new SituationReportViewModel
        {
            GeneratedAt = now,
            OpenNeeds = openNeeds.Count,
            AcuteOpenNeeds = openNeeds.Count(need => need.Priority == NeedPriority.Acute),
            OverdueNeeds = openNeeds.Count(need =>
                need.Deadline.HasValue && need.Deadline.Value.Date.AddDays(1) < now),
            AssignedNeeds = openNeeds.Count(need => need.Status == NeedStatus.Assigned),
            ResolvedNeeds = needs.Count(need => need.Status == NeedStatus.Resolved),
            CriticalNeeds = urgentRows.Count(row => row.Urgency.Level == UrgencyLevel.Critical),
            HighNeeds = urgentRows.Count(row => row.Urgency.Level == UrgencyLevel.High),
            MediumNeeds = urgentRows.Count(row => row.Urgency.Level == UrgencyLevel.Medium),
            LowNeeds = urgentRows.Count(row => row.Urgency.Level == UrgencyLevel.Low),
            AvailableResources = resourceStatuses.Count(resource => resource.Status == ResourceStatus.Available),
            BusyResources = resourceStatuses.Count(resource => resource.Status == ResourceStatus.Busy),
            TotalResources = resourceStatuses.Count,
            PendingRequests = pendingRequests,
            ConfirmedLast24h = confirmedLast24h,
            Coverage = coverage,
            MostUrgent = urgentRows.Take(8).ToList(),
            Uncovered = urgentRows
                .Where(row => row.Urgency.NearbyResources == 0 && !row.IsAssigned)
                .Take(10)
                .ToList(),
            Municipalities = resourceStatuses
                .GroupBy(resource => ResourceDisplay.Municipality(resource.Address), StringComparer.OrdinalIgnoreCase)
                .Select(group => new MunicipalityRow
                {
                    Municipality = group.Key,
                    Available = group.Count(resource => resource.Status == ResourceStatus.Available),
                    Total = group.Count()
                })
                .OrderByDescending(row => row.Total)
                .Take(12)
                .ToList()
        };

        model.AutomaticSummary = Summarize(model);

        return model;
    }


    /// <summary>
    /// Regelbasert oppsummering, brukt når AI ikke er tilgjengelig
    /// og som fasit Sami skal holde seg til.
    /// </summary>
    public static List<string> Summarize(SituationReportViewModel report)
    {
        var lines = new List<string>();

        if (report.OpenNeeds == 0)
        {
            lines.Add("Ingen åpne behov akkurat nå. Situasjonen er rolig.");
        }
        else
        {
            lines.Add(
                $"{ResourceDisplay.CountText(report.OpenNeeds, "åpent behov", "åpne behov")}, "
                + $"hvorav {report.AcuteOpenNeeds} akutt{(report.AcuteOpenNeeds == 1 ? "" : "e")} "
                + $"og {report.AssignedNeeds} allerede tildelt.");
        }

        if (report.CriticalNeeds > 0)
        {
            lines.Add($"{ResourceDisplay.CountText(report.CriticalNeeds, "behov har", "behov har")} kritisk hastegrad og bør følges opp først.");
        }

        if (report.OverdueNeeds > 0)
        {
            lines.Add($"{ResourceDisplay.CountText(report.OverdueNeeds, "behov har", "behov har")} passert fristen.");
        }

        List<string> gaps =
            report.Coverage.Where(row => row.IsGap).Select(row => row.TypeName.ToLowerInvariant()).ToList();

        if (gaps.Count > 0)
        {
            lines.Add($"Mangler tilgjengelige ressurser av typen {JoinNorwegian(gaps)}. Vurder å be om hjelp utenfra.");
        }

        List<string> tight =
            report.Coverage.Where(row => row.IsTight).Select(row => row.TypeName.ToLowerInvariant()).ToList();

        if (tight.Count > 0)
        {
            lines.Add($"Knapp dekning for {JoinNorwegian(tight)}: færre tilgjengelige ressurser enn åpne behov.");
        }

        if (report.Uncovered.Count > 0)
        {
            lines.Add($"{ResourceDisplay.CountText(report.Uncovered.Count, "behov har", "behov har")} ingen passende ressurs innen {NeedUrgencyService.CoverageRadiusKm:0} km.");
        }

        if (report.PendingRequests > 0)
        {
            lines.Add($"{ResourceDisplay.CountText(report.PendingRequests, "forespørsel venter", "forespørsler venter")} på svar fra frivillige.");
        }

        lines.Add(
            $"{report.AvailableResources} av {report.TotalResources} registrerte ressurser er tilgjengelige nå"
            + (report.BusyResources > 0 ? $", {report.BusyResources} er opptatt." : "."));

        return lines;
    }


    /// <summary>
    /// «a», «a og b», «a, b og c».
    /// </summary>
    public static string JoinNorwegian(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => "",
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " og " + items[^1]
        };
    }


    /// <summary>
    /// Tallgrunnlaget Sami får. Bare tall og korte tekster,
    /// ingen navn, e-poster eller telefonnumre (personvern).
    /// </summary>
    public static string BuildAiFacts(SituationReportViewModel report)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"Tidspunkt (UTC): {report.GeneratedAt:yyyy-MM-dd HH:mm}");
        builder.AppendLine($"Åpne behov: {report.OpenNeeds} (akutte: {report.AcuteOpenNeeds}, tildelt: {report.AssignedNeeds}, passert frist: {report.OverdueNeeds})");
        builder.AppendLine($"Hastegrad: kritisk {report.CriticalNeeds}, høy {report.HighNeeds}, middels {report.MediumNeeds}, lav {report.LowNeeds}");
        builder.AppendLine($"Løste behov totalt: {report.ResolvedNeeds}");
        builder.AppendLine($"Ressurser: {report.AvailableResources} tilgjengelige, {report.BusyResources} opptatt, {report.TotalResources} totalt");
        builder.AppendLine($"Forespørsler som venter på svar: {report.PendingRequests}. Bekreftet siste døgn: {report.ConfirmedLast24h}");

        builder.AppendLine("Dekning per ressurstype (åpne, ikke tildelte behov / tilgjengelige ressurser):");
        foreach (TypeCoverageRow row in report.Coverage)
        {
            string flag = row.IsGap ? " MANGLER" : row.IsTight ? " KNAPP" : "";
            builder.AppendLine($"- {row.TypeName}: {row.OpenNeeds} / {row.AvailableResources}{flag}");
        }

        builder.AppendLine("Mest presserende behov:");
        foreach (UrgentNeedRow row in report.MostUrgent.Take(5))
        {
            string deadline = row.Deadline.HasValue ? $", frist {row.Deadline.Value:dd.MM}" : "";
            string description = row.Description.Length > 120 ? row.Description[..120] + "…" : row.Description;
            builder.AppendLine(
                $"- [{row.Urgency.LevelName}, {row.Urgency.Score} poeng] {row.TypeName}, {row.Quantity} stk, {row.PriorityName}{deadline}: {description} "
                + $"(grunner: {string.Join("; ", row.Urgency.Reasons)})");
        }

        builder.AppendLine("Ressurser per kommune (tilgjengelige / totalt):");
        foreach (MunicipalityRow row in report.Municipalities)
        {
            builder.AppendLine($"- {row.Municipality}: {row.Available} / {row.Total}");
        }

        return builder.ToString();
    }


    public const string AiSystemPrompt =
        "Du er Sami, assistenten i Beredskapsportalen for Heimevernet og offentlige aktører. "
        + "Du skal skrive en kort situasjonsrapport på norsk bokmål til en beredskapsleder. "
        + "Bruk KUN tallene og opplysningene du får. Ikke finn på tall, steder eller hendelser. "
        + "Skriv nøkternt og konkret, uten innledning og uten avslutningshilsen. "
        + "Bruk nøyaktig disse tre overskriftene, hver på egen linje: "
        + "«Situasjonsbilde» (2–3 setninger), «Viktigste risikoer» (2–4 punkter som starter med «- ») "
        + "og «Anbefalte tiltak» (3 punkter som starter med «- »). "
        + "Maks 180 ord totalt. Ikke bruk markdown som ** eller #.";
}
