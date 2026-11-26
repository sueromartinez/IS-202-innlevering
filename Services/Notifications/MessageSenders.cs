/*
    MessageSenders.cs – e-post og SMS

    Hva filen gjør: SmtpEmailSender sender ekte e-post. Lokalt fanges den av Mailpit i Docker,
    så man kan lese den på http://localhost:8025 uten at noe sendes ut. SimulatedSmsSender
    skriver bare SMS-en i loggen.
    For ekte SMS lager man en ny klasse og bytter én linje i Program.cs.
*/

using System.Net.Mail;

namespace FirstWebAppInDocker.Services.Notifications;

/*
    Innstillinger for e-post. Leses fra seksjonen "Smtp"
    i appsettings.json eller miljøvariabler (Smtp__Host osv.).

    I docker-compose peker dette på Mailpit, en lokal test-e-postserver
    som fanger alle e-poster og viser dem på http://localhost:8025.
*/
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

/*
    Sender e-post via SMTP. Hvis ingen SMTP-server er satt opp,
    logges e-posten i stedet, slik at appen fortsatt fungerer.
*/
public class SmtpEmailSender : IPortalEmailSender
{
    #region Felt og konstruktør

    private readonly SmtpOptions _options;

    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(
        Microsoft.Extensions.Options.IOptions<SmtpOptions> options,
        ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    #endregion

    #region Send e-post

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

        using MailMessage message = new MailMessage(_options.From, toEmail, subject, body);

        using SmtpClient client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl
        };

        await client.SendMailAsync(message);
    }

    #endregion
}

/*
    Simulert SMS. Ekte SMS krever en betalt leverandør
    (f.eks. Twilio eller Sveve). Bytt ut denne klassen med en
    som kaller leverandørens API når det blir aktuelt.
*/
public class SimulatedSmsSender : ISmsSender
{
    #region Felt og konstruktør

    private readonly ILogger<SimulatedSmsSender> _logger;

    public SimulatedSmsSender(ILogger<SimulatedSmsSender> logger)
    {
        _logger = logger;
    }

    #endregion

    #region Send SMS

    public Task SendAsync(string phoneNumber, string message)
    {
        _logger.LogInformation("[Simulert SMS] til {Phone}: {Message}", phoneNumber, message);

        return Task.CompletedTask;
    }

    #endregion
}
