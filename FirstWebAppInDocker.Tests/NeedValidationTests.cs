
using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class NeedValidationTests
{
    // ========================================
    // HJELPER - OPPRETT GYLDIG BEHOV
    // ========================================

    private static NeedItemViewModel CreateValidNeed(
        ResourceType? resourceType = ResourceType.Generator,
        string description = "Behov for reservekraft",
        int quantity = 2)
    {
        return new NeedItemViewModel
        {
            ResourceType = resourceType,
            Priority = NeedPriority.Acute,
            Quantity = quantity,
            Description = description,
            Latitude = 58.1467,
            Longitude = 7.9956
        };
    }


    // ========================================
    // HJELPER - VALIDERING
    // ========================================

    private static List<ValidationResult> Validate(
        NeedItemViewModel model)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true
        );

        return results;
    }


    // ========================================
    // TEST 1 - GYLDIG BEHOV
    // ========================================

    [Fact]
    public void ValidNeed_ShouldPassValidation()
    {
        var model = CreateValidNeed();

        var results = Validate(model);

        Assert.Empty(results);
    }


    // ========================================
    // TEST 2 - MANGLENDE RESSURSTYPE
    // ========================================

    [Fact]
    public void MissingResourceType_ShouldFailValidation()
    {
        var model = CreateValidNeed(
            resourceType: null
        );

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(NeedItemViewModel.ResourceType)
            )
        );
    }


    // ========================================
    // TEST 3 - MANGLENDE BESKRIVELSE
    // ========================================

    [Fact]
    public void MissingDescription_ShouldFailValidation()
    {
        var model = CreateValidNeed(
            description: ""
        );

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(NeedItemViewModel.Description)
            )
        );
    }


    // ========================================
    // TEST 4 - BESKRIVELSE MED BARE MELLOMROM
    // ========================================

    [Fact]
    public void WhitespaceDescription_ShouldFailValidation()
    {
        var model = CreateValidNeed(
            description: "   "
        );

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(NeedItemViewModel.Description)
            )
        );
    }


    // ========================================
    // TEST 5 - BESKRIVELSE OVER 500 TEGN
    // ========================================

    [Fact]
    public void DescriptionOver500Characters_ShouldFailValidation()
    {
        var model = CreateValidNeed(
            description: new string('A', 501)
        );

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(NeedItemViewModel.Description)
            )
        );
    }


    // ========================================
    // TEST 6 - NØYAKTIG 500 TEGN ER LOV
    // ========================================

    [Fact]
    public void DescriptionWith500Characters_ShouldPassValidation()
    {
        var model = CreateValidNeed(
            description: new string('A', 500)
        );

        var results = Validate(model);

        Assert.Empty(results);
    }


    // ========================================
    // TEST 7 OG 8 - ANTALL FOR LAVT
    // ========================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void QuantityBelowMinimum_ShouldFailValidation(
        int quantity)
    {
        var model = CreateValidNeed(
            quantity: quantity
        );

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(NeedItemViewModel.Quantity)
            )
        );
    }


    // ========================================
    // TEST 9 OG 10 - ANTALL FOR HØYT
    // ========================================

    [Theory]
    [InlineData(10000)]
    [InlineData(20000)]
    public void QuantityAboveMaximum_ShouldFailValidation(
        int quantity)
    {
        var model = CreateValidNeed(
            quantity: quantity
        );

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(NeedItemViewModel.Quantity)
            )
        );
    }


    // ========================================
    // TEST 11 OG 12 - GYLDIGE GRENSEVERDIER
    // ========================================

    [Theory]
    [InlineData(1)]
    [InlineData(9999)]
    public void QuantityWithinValidBoundaries_ShouldPassValidation(
        int quantity)
    {
        var model = CreateValidNeed(
            quantity: quantity
        );

        var results = Validate(model);

        Assert.Empty(results);
    }


    // ========================================
    // TEST 13 - STANDARDPRIORITET
    // ========================================

    [Fact]
    public void NewNeed_ShouldDefaultToAcutePriority()
    {
        var model = new NeedItemViewModel();

        Assert.Equal(
            NeedPriority.Acute,
            model.Priority
        );
    }


    // ========================================
    // TEST 14 - FLERE BEHOV I SAMME SKJEMA
    // ========================================

    [Fact]
    public void Registration_ShouldSupportMultipleNeeds()
    {
        var model = new RegisterNeedsViewModel();

        model.Needs.Add(
            CreateValidNeed(
                description: "Behov for aggregat"
            )
        );

        model.Needs.Add(
            CreateValidNeed(
                description: "Behov for ekstra strømforsyning"
            )
        );

        Assert.Equal(
            2,
            model.Needs.Count
        );

        Assert.All(
            model.Needs,
            need => Assert.Empty(Validate(need))
        );
    }
}
