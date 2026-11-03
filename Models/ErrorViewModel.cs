namespace FirstWebAppInDocker.Models;

/// <summary>
/// Modell for visning av feilinformasjon.
/// </summary>
public class ErrorViewModel
{
    public string? RequestId { get; set; }


    public bool ShowRequestId =>
        !string.IsNullOrEmpty(RequestId);
}