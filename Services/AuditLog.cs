using System.Security.Claims;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.AspNetCore.Mvc;

namespace FirstWebAppInDocker.Services;

/// <summary>
/// Kodene for handlingene som logges, med norsk visningsnavn.
/// </summary>
public static class AuditActions
{
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string ResourceCreated = "ResourceCreated";
    public const string ResourceUpdated = "ResourceUpdated";
    public const string ResourceDeleted = "ResourceDeleted";
    public const string ResourceStatusChanged = "ResourceStatusChanged";
    public const string NeedCreated = "NeedCreated";
    public const string NeedUpdated = "NeedUpdated";
    public const string NeedDeleted = "NeedDeleted";
    public const string RequestSent = "RequestSent";
    public const string RequestAnswered = "RequestAnswered";
    public const string RequestCancelled = "RequestCancelled";
    public const string NeedResolved = "NeedResolved";
    public const string RoleChanged = "RoleChanged";
    public const string UserCreated = "UserCreated";
    public const string UserDeactivated = "UserDeactivated";
    public const string UserReactivated = "UserReactivated";
    public const string Export = "Export";
    public const string ReportGenerated = "ReportGenerated";


    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [Login] = "Innlogging",
        [LoginFailed] = "Mislykket innlogging",
        [Logout] = "Utlogging",
        [ResourceCreated] = "Ressurs registrert",
        [ResourceUpdated] = "Ressurs endret",
        [ResourceDeleted] = "Ressurs slettet",
        [ResourceStatusChanged] = "Ressursstatus endret",
        [NeedCreated] = "Behov registrert",
        [NeedUpdated] = "Behov endret",
        [NeedDeleted] = "Behov slettet",
        [RequestSent] = "Forespørsel sendt",
        [RequestAnswered] = "Forespørsel besvart",
        [RequestCancelled] = "Forespørsel avbrutt",
        [NeedResolved] = "Behov løst",
        [RoleChanged] = "Rolle endret",
        [UserCreated] = "Bruker opprettet",
        [UserDeactivated] = "Bruker deaktivert",
        [UserReactivated] = "Bruker reaktivert",
        [Export] = "Eksport",
        [ReportGenerated] = "Situasjonsrapport"
    };


    public static string NameOf(string action)
    {
        return Names.TryGetValue(action, out string? name) ? name : action;
    }
}


/// <summary>
/// Skriver linjer til audit-loggen.
/// Feil ved logging skal aldri stoppe selve handlingen, så de logges bare som advarsel.
/// </summary>
public class AuditService
{
    private readonly ApplicationDbContext _context;

    private readonly ILogger<AuditService> _logger;


    public AuditService(
        ApplicationDbContext context,
        ILogger<AuditService> logger
    )
    {
        _context = context;
        _logger = logger;
    }


    public async Task LogAsync(
        ClaimsPrincipal? user,
        string action,
        string details,
        string? ipAddress = null,
        string? userIdOverride = null,
        string? userEmailOverride = null
    )
    {
        AuditLogEntry? entry = null;

        try
        {
            string? userId =
                userIdOverride ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);

            string? userEmail =
                userEmailOverride
                ?? user?.FindFirstValue(ClaimTypes.Email)
                ?? user?.Identity?.Name;

            entry = new AuditLogEntry
            {
                Timestamp = DateTime.UtcNow,
                UserId = Truncate(userId, 255),
                UserEmail = Truncate(userEmail, 150),
                Action = Truncate(action, 60) ?? action,
                Details = Truncate(details, 500) ?? string.Empty,
                IpAddress = Truncate(ipAddress, 45)
            };

            _context.AuditLog.Add(entry);

            await _context.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Kunne ikke skrive til audit-loggen ({Action})", action);

            // Fjern linjen igjen, så den ikke blir lagret av en senere SaveChanges.
            if (entry is not null)
            {
                _context.Entry(entry).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            }
        }
    }


    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}


/// <summary>
/// Gjør det enkelt å logge fra en controller: await this.AuditAsync(...).
///
/// Tjenesten hentes fra forespørselen i stedet for konstruktøren,
/// slik at eksisterende controllere og tester ikke må endres.
/// I enhetstester finnes ingen tjenester, og da gjøres ingenting.
/// </summary>
public static class AuditControllerExtensions
{
    public static async Task AuditAsync(
        this ControllerBase controller,
        string action,
        string details,
        string? userIdOverride = null,
        string? userEmailOverride = null
    )
    {
        HttpContext? httpContext = controller.HttpContext;

        if (httpContext?.RequestServices?.GetService(typeof(AuditService)) is not AuditService audit)
        {
            return;
        }

        await audit.LogAsync(
            httpContext.User,
            action,
            details,
            httpContext.Connection?.RemoteIpAddress?.ToString(),
            userIdOverride,
            userEmailOverride);
    }
}
