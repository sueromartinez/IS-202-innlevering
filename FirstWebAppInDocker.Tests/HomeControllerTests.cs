using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class ResourceValidationTests
{
    // Oppretter en gyldig ressurs som
    // brukes som utgangspunkt i testene.

    private static RegisterResourceViewModel
        CreateValidResource()
    {
        return new RegisterResourceViewModel
        {
            ResourceType = ResourceType.Generator,
            ResourceName = "Aggregat",
            Description = "Reservekraft ved strømbrudd",
            Quantity = 2,

            Capacity = "25 kVA",
            Availability = "Tilgjengelig",

            Address = "Kristiansand",
            Latitude = 58.1467,
            Longitude = 7.9956,

            ContactEmail = "test@example.com",
            ContactPhoneCountryCode = "+47",
            ContactPhoneNumber = "12345678"
        };
    }


    // ========================================
    // TEST 1 - GYLDIG RESSURS
    // ========================================

    [Fact]
    public void ValidResource_ShouldPassValidation()
    {
        var model = CreateValidResource();

        var results = new List<ValidationResult>();

        bool isValid = Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true
        );

        Assert.True(isValid);
        Assert.Empty(results);
    }


    // ========================================
    // TEST 2 - MANGLENDE NAVN
    // ========================================

    [Fact]
    public void MissingResourceName_ShouldFailValidation()
    {
        var model = CreateValidResource();

        model.ResourceName = "";

        var results = new List<ValidationResult>();

        bool isValid = Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true
        );

        Assert.False(isValid);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(model.ResourceName)
            )
        );
    }


    // ========================================
    // TEST 3 - UGYLDIG ANTALL
    // ========================================

    [Fact]
    public void QuantityZero_ShouldFailValidation()
    {
        var model = CreateValidResource();

        model.Quantity = 0;

        var results = new List<ValidationResult>();

        bool isValid = Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true
        );

        Assert.False(isValid);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(model.Quantity)
            )
        );
    }


    // ========================================
    // TEST 4 - UGYLDIG BREDDEGRAD
    // ========================================

    [Fact]
    public void InvalidLatitude_ShouldFailValidation()
    {
        var model = CreateValidResource();

        model.Latitude = 100;

        var results = model.Validate(
            new ValidationContext(model)
        ).ToList();

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(model.Latitude)
            )
        );
    }


    // ========================================
    // TEST 5 - UGYLDIG LENGDEGRAD
    // ========================================

    [Fact]
    public void InvalidLongitude_ShouldFailValidation()
    {
        var model = CreateValidResource();

        model.Longitude = 200;

        var results = model.Validate(
            new ValidationContext(model)
        ).ToList();

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(model.Longitude)
            )
        );
    }
}