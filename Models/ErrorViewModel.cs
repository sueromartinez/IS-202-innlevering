/*
    ErrorViewModel.cs – feilsiden

    Hva filen gjør: Standardfil fra ASP.NET-malen, brukt av feilsiden. Trenger ikke forklares.
*/

namespace FirstWebAppInDocker.Models;

// Modell for visning av feilinformasjon.
public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
