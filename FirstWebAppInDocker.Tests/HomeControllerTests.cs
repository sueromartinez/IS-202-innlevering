using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class ResourceValidationTests
{
    // Tester servervalideringen uten nettleser eller ekte database.
    [Theory]
    [InlineData(null, false)]
    [InlineData(5, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(6, true)]
    [InlineData(12, true)]
    [InlineData(24, true)]
    [InlineData(36, true)]
    public void AvailabilityPeriod_MustBeSupported(int? months, bool expectedValid)
    {
        var model = CreateValidResource();
        model.AvailabilityMonths = months;
        var errors = new List<ValidationResult>();
        Assert.Equal(expectedValid, Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
    }

    [Fact]
    public void CombinedAddress_RejectsOverflow_AndKeepsLeadingZeroInPostalCode()
    {
        var model = CreateValidResource();
        model.StreetAddress = "Testveien 1";
        model.PostalLocation = "0150, Oslo";
        model.Municipality = "Oslo";
        Assert.Contains("0150, Oslo", model.CombinedAddress());
        model.StreetAddress = new string('x', 195);
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
        Assert.Contains(errors, error => error.MemberNames.Contains(nameof(model.StreetAddress)));
    }

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
            AvailabilityMonths = 3,

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
