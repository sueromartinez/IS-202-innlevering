/*
    NeedValidationTests.cs – automatiske tester

    Hva filen gjør: Tester reglene for gyldige behov. Bruker xUnit, en database i minnet (EF
    Core InMemory) og Moq, så testene kjører raskt uten Docker.

    Oppgavekrav: 6, 15
*/

using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class NeedValidationTests
{
    // --- Hjelper: opprett gyldig behov ---

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

    // --- Hjelper: validering ---

    private static List<ValidationResult> Validate(NeedItemViewModel model)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true);

        return results;
    }

    // --- Test 1: gyldig behov ---

    [Fact]
    public void ValidNeed_ShouldPassValidation()
    {
        var model = CreateValidNeed();

        var results = Validate(model);

        Assert.Empty(results);
    }

    // --- Test 2: manglende ressurstype ---

    [Fact]
    public void MissingResourceType_ShouldFailValidation()
    {
        var model = CreateValidNeed(resourceType: null);

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(NeedItemViewModel.ResourceType)));
    }

    // --- Test 3: manglende beskrivelse ---

    [Fact]
    public void MissingDescription_ShouldFailValidation()
    {
        var model = CreateValidNeed(description: "");

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(NeedItemViewModel.Description)));
    }

    // --- Test 4: beskrivelse med bare mellomrom ---

    [Fact]
    public void WhitespaceDescription_ShouldFailValidation()
    {
        var model = CreateValidNeed(description: "   ");

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(NeedItemViewModel.Description)));
    }

    // --- Test 5: beskrivelse over 500 tegn ---

    [Fact]
    public void DescriptionOver500Characters_ShouldFailValidation()
    {
        var model = CreateValidNeed(description: new string('A', 501));

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(NeedItemViewModel.Description)));
    }

    // --- Test 6: nøyaktig 500 tegn er lov ---

    [Fact]
    public void DescriptionWith500Characters_ShouldPassValidation()
    {
        var model = CreateValidNeed(description: new string('A', 500));

        var results = Validate(model);

        Assert.Empty(results);
    }

    // --- Test 7 og 8: antall for lavt ---

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void QuantityBelowMinimum_ShouldFailValidation(int quantity)
    {
        var model = CreateValidNeed(quantity: quantity);

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(NeedItemViewModel.Quantity)));
    }

    // --- Test 9 og 10: antall for høyt ---

    [Theory]
    [InlineData(10000)]
    [InlineData(20000)]
    public void QuantityAboveMaximum_ShouldFailValidation(int quantity)
    {
        var model = CreateValidNeed(quantity: quantity);

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(NeedItemViewModel.Quantity)));
    }

    // --- Test 11 og 12: gyldige grenseverdier ---

    [Theory]
    [InlineData(1)]
    [InlineData(9999)]
    public void QuantityWithinValidBoundaries_ShouldPassValidation(int quantity)
    {
        var model = CreateValidNeed(quantity: quantity);

        var results = Validate(model);

        Assert.Empty(results);
    }

    // --- Test 13: standardprioritet ---

    [Fact]
    public void NewNeed_ShouldDefaultToAcutePriority()
    {
        var model = new NeedItemViewModel();

        Assert.Equal(NeedPriority.Acute, model.Priority);
    }

    // --- Test 14: flere behov i samme skjema ---

    [Fact]
    public void Registration_ShouldSupportMultipleNeeds()
    {
        var model = new RegisterNeedsViewModel();

        model.Needs.Add(CreateValidNeed(description: "Behov for aggregat"));

        model.Needs.Add(CreateValidNeed(description: "Behov for ekstra strømforsyning"));

        Assert.Equal(2, model.Needs.Count);

        Assert.All(model.Needs, need => Assert.Empty(Validate(need)));
    }
}
