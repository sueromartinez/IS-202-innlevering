using System.Net.Mail;

namespace FirstWebAppInDocker.Services.Notifications;

/// <summary>
/// Innstillinger for e-post. Leses fra seksjonen "Smtp"
/// i appsettings.json eller miljøvariabler (Smtp__Host osv.).
///
/// I docker-compose peker dette på Mailpit, en lokal test-e-postserver
/// som fanger alle e-poster og viser dem på http://localhost:8025.
/// </summary>
public class SmtpOptions
{
    public string? Host { get; set; }

    public int Port { get; set; } = 1025;

    public bool EnableSsl { get; set; }

    public string From { get; set; } = "beredskapsportal@localhost";
}


public interface IPortalEmailSender
{
    Task SendAsync(string toEmail, string subject, string body);
}


public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message);
}


/// <summary>
/// Sender e-post via SMTP. Hvis ingen SMTP-server er satt opp,
/// logges e-posten i stedet, slik at appen fortsatt fungerer.
/// </summary>
public class SmtpEmailSender : IPortalEmailSender
{
    private readonly SmtpOptions _options;

    private readonly ILogger<SmtpEmailSender> _logger;


    public SmtpEmailSender(
        Microsoft.Extensions.Options.IOptions<SmtpOptions> options,
        ILogger<SmtpEmailSender> logger
    )
    {
        _options = options.Value;
        _logger = logger;
    }


    public async Task SendAsync(string toEmail, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
        {
            _logger.LogInformation(
                "E-post (ikke sendt, SMTP er ikke satt opp) til {To}: {Subject}",
                toEmail,
                subject);

            return;
        }

        using var message = new MailMessage(_options.From, toEmail, subject, body);

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl
        };

        await client.SendMailAsync(message);
    }
}


/// <summary>
/// Simulert SMS. Ekte SMS krever en betalt leverandør
/// (f.eks. Twilio eller Sveve). Bytt ut denne klassen med en
/// som kaller leverandørens API når det blir aktuelt.
/// </summary>
public class SimulatedSmsSender : ISmsSender
{
    private readonly ILogger<SimulatedSmsSender> _logger;


    public SimulatedSmsSender(ILogger<SimulatedSmsSender> logger)
    {
        _logger = logger;
    }


    public Task SendAsync(string phoneNumber, string message)
    {
        _logger.LogInformation(
            "[Simulert SMS] til {Phone}: {Message}",
            phoneNumber,
            message);

        return Task.CompletedTask;
    }
}
