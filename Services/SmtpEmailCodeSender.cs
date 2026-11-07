
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace FirstWebAppInDocker.Services;

// ========================================
// GRENSESNITT FOR E-POSTUTSENDING
// ========================================

public interface IEmailCodeSender
{
    Task SendCodeAsync(
        string email,
        string code,
        CancellationToken cancellationToken = default);

    Task SendConfirmationAsync(
        string email,
        string confirmationUrl,
        CancellationToken cancellationToken = default);
}

// ========================================
// SMTP E-POSTTJENESTE
// ========================================

public sealed class SmtpEmailCodeSender : IEmailCodeSender
{
    private readonly IConfiguration _configuration;

    public SmtpEmailCodeSender(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // ========================================
    // SEND SEKSSIFRET INNLOGGINGSKODE
    // ========================================

    public async Task SendCodeAsync(
        string email,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(code, @"^[0-9]{6}$"))
        {
            throw new ArgumentException(
                "Sikkerhetskoden må inneholde seks sifre.",
                nameof(code));
        }

        string subject =
            "Din sikkerhetskode - Beredskapsportalen";

        string body =
            "Hei!\n\n" +
            "Du forsøker å logge inn på Beredskapsportalen.\n\n" +
            "Din sikkerhetskode er:\n\n" +
            code + "\n\n" +
            "Skriv inn koden på nettsiden for å " +
            "fullføre innloggingen.\n\n" +
            "Ikke del koden med andre.\n\n" +
            "Hvis du ikke forsøker å logge inn, " +
            "kan du ignorere denne meldingen.\n\n" +
            "Hilsen Beredskapsportalen";

        await SendEmailAsync(
            email,
            subject,
            body,
            cancellationToken);
    }

    // ========================================
    // SEND BEKREFTELSE VED REGISTRERING
    // ========================================

    public async Task SendConfirmationAsync(
        string email,
        string confirmationUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(
            confirmationUrl,
            UriKind.Absolute,
            out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException(
                "Ugyldig bekreftelseslenke.",
                nameof(confirmationUrl));
        }

        string subject =
            "Bekreft e-postadressen din - Beredskapsportalen";

        string body =
            "Hei!\n\n" +
            "Velkommen til Beredskapsportalen!\n\n" +
            "For å aktivere brukerkontoen din må du " +
            "bekrefte e-postadressen din.\n\n" +
            "Åpne denne lenken:\n\n" +
            confirmationUrl + "\n\n" +
            "Hvis du ikke har registrert en konto, " +
            "kan du ignorere denne e-posten.\n\n" +
            "Hilsen Beredskapsportalen";

        await SendEmailAsync(
            email,
            subject,
            body,
            cancellationToken);
    }

    // ========================================
    // FELLES SMTP-UTSENDING
    // ========================================

    private async Task SendEmailAsync(
        string email,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException(
                "E-postadresse mangler.",
                nameof(email));
        }

        string host =
            _configuration["Email:Smtp:Host"]
            ?? throw new InvalidOperationException(
                "SMTP-server er ikke konfigurert.");

        string from =
            _configuration["Email:Smtp:From"]
            ?? throw new InvalidOperationException(
                "Avsenderadresse er ikke konfigurert.");

        int port =
            _configuration.GetValue<int?>(
                "Email:Smtp:Port") ?? 587;

        bool enableSsl =
            _configuration.GetValue<bool?>(
                "Email:Smtp:EnableSsl") ?? true;

        string? username =
            _configuration["Email:Smtp:Username"];

        string? password =
            _configuration["Email:Smtp:Password"];

        using var message = new MailMessage
        {
            From = new MailAddress(
                from,
                "Beredskapsportalen"),

            Subject = subject,

            Body = body,

            IsBodyHtml = false
        };

        message.To.Add(new MailAddress(email));

        using var smtp = new SmtpClient(host, port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,

            EnableSsl = enableSsl,

            UseDefaultCredentials = false
        };

        if (!string.IsNullOrWhiteSpace(username))
        {
            smtp.Credentials = new NetworkCredential(
                username,
                password ?? string.Empty);
        }

        await smtp.SendMailAsync(
            message,
            cancellationToken);
    }
}
