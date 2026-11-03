using System.ComponentModel.DataAnnotations;

namespace FirstWebAppInDocker.Models;

/// <summary>
/// Representerer en ressurs som lagres
/// permanent i databasen.
/// </summary>
public class Resource
{
    /// <summary>
    /// Unik identifikator.
    /// </summary>
    public int Id { get; init; }


    [Required]
    public ResourceType ResourceType { get; init; }


    [Required]
    [StringLength(500)]
    public string Description { get; init; } = string.Empty;


    public double Latitude { get; init; }


    public double Longitude { get; init; }


    [EmailAddress]
    [StringLength(150)]
    public string? ContactEmail { get; init; }


    [StringLength(5)]
    public string? ContactPhoneCountryCode { get; init; }


    [StringLength(20)]
    public string? ContactPhoneNumber { get; init; }


    /// <summary>
    /// Midlertidig eieridentifikasjon.
    /// Dette erstattes av Identity UserId senere.
    /// </summary>
    [Required]
    [StringLength(150)]
    public string OwnerEmail { get; init; } = string.Empty;


    /// <summary>
    /// Tidspunkt ressursen ble registrert.
    /// </summary>
    public DateTime CreatedAt { get; init; } =
        DateTime.UtcNow;
}