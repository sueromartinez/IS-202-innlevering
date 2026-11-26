/*
    NotificationService.cs – sender varsler

    Hva filen gjør: Ett kall gjør tre ting: lagrer varselet i databasen (varselklokka), sender
    e-post, og sender SMS hvis brukeren har telefonnummer (SMS er foreløpig simulert).

    På nettsiden: Tallet på varselklokka, listen under «Varsler», og e-posten brukeren får.

    Oppgavekrav: 10, 19
*/

using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using Microsoft.EntityFrameworkCore;

namespace FirstWebAppInDocker.Services.Notifications;

/*
    Varsler en bruker på tre måter:
    1. Varsel i portalen (varselklokken) – fungerer alltid.
    2. E-post – via SMTP/Mailpit.
    3. SMS – simulert i prototypen.

    Feil ved e-post eller SMS stopper ikke resten av flyten;
    varselet i portalen er det som alltid lagres.
*/
public class NotificationService
{
    #region Felt og konstruktør

    private readonly ApplicationDbContext _context;

    private readonly IPortalEmailSender _emailSender;

    private readonly ISmsSender _smsSender;

    private readonly IConfiguration _configuration;

    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext context,
        IPortalEmailSender emailSender,
        ISmsSender smsSender,
        IConfiguration configuration,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _emailSender = emailSender;
        _smsSender = smsSender;
        _configuration = configuration;
        _logger = logger;
    }

    #endregion

    #region Send varsel

    /*
        Lagrer et varsel og sender det på e-post (og SMS hvis
        telefonnummer er oppgitt).

        link: Relativ lenke i portalen, f.eks. "/Request".
        phoneNumber: Telefonnummer med landkode, eller null.
    */
    public async Task NotifyAsync(
        string userId,
        string title,
        string message,
        string? link = null,
        string? phoneNumber = null)
    {
        // Først lagres varselet i portalen. Det er den sikreste kanalen.
        _context.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = Truncate(title, 150),
            Message = Truncate(message, 500),
            Link = link,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        string? email =
            await _context.Users
                .Where(user => user.Id == userId)
                .Select(user => user.Email)
                .FirstOrDefaultAsync();

        string baseUrl =
            (_configuration["App:BaseUrl"] ?? "http://localhost:8080")
                .TrimEnd('/');

        string fullLink = link is null ? baseUrl : baseUrl + link;

        /*
            Er e-postserveren nede, skal ikke hele handlingen feile. Feilen logges, og varselet
            finnes uansett i portalen.
        */
        if (!string.IsNullOrWhiteSpace(email))
        {
            try
            {
                await _emailSender.SendAsync(
                    email,
                    $"Beredskapsportal: {title}",
                    $"{message}\n\nSe mer og svar her: {fullLink}\n\n"
                    + "Denne e-posten er sendt automatisk fra Beredskapsportalen.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Kunne ikke sende e-post til {Email}", email);
            }
        }

        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            try
            {
                await _smsSender.SendAsync(
                    phoneNumber,
                    $"Beredskapsportal: {title}. Logg inn for å svare: {fullLink}");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Kunne ikke sende SMS");
            }
        }
    }

    #endregion

    #region Hjelpemetoder

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..(maxLength - 1)] + "…";
    }

    #endregion
}
