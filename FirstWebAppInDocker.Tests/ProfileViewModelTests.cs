/*
    ProfileViewModelTests.cs – automatiske tester

    Hva filen gjør: Tester reglene i profilskjemaet. Bruker xUnit, en database i minnet (EF Core
    InMemory) og Moq, så testene kjører raskt uten Docker.

    Oppgavekrav: 6, 15
*/

using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.ViewModels;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class ProfileViewModelTests
{
    // --- Hjelper: gyldig profil ---

    private static ProfileViewModel CreateProfile(
        string name = "Ola Nordmann",
        string email = "ola@example.com",
        string countryCode = "+47",
        string? phoneNumber = "12345678")
    {
        return new ProfileViewModel
        {
            Name = name,
            Email = email,
            PhoneCountryCode = countryCode,
            PhoneNumber = phoneNumber
        };
    }

    // --- Hjelper: validering ---

    private static List<ValidationResult> Validate(ProfileViewModel model)
    {
        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true);

        return results;
    }

    // --- Test 1: gyldig profil ---

    [Fact]
    public void ValidProfile_ShouldPassValidation()
    {
        var model = CreateProfile();

        var results = Validate(model);

        Assert.Empty(results);
    }

    // --- Test 2: manglende navn ---

    [Fact]
    public void MissingName_ShouldFailValidation()
    {
        var model = CreateProfile(name: "");

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(ProfileViewModel.Name)));
    }

    // --- Test 3: ugyldig e-post ---

    [Fact]
    public void InvalidEmail_ShouldFailValidation()
    {
        var model = CreateProfile(email: "ugyldig-epost");

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(ProfileViewModel.Email)));
    }

    // --- Test 4: ugyldig norsk telefonnummer ---

    [Fact]
    public void NorwegianPhoneWithSevenDigits_ShouldFailValidation()
    {
        var model = CreateProfile(countryCode: "+47", phoneNumber: "1234567");

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(ProfileViewModel.PhoneNumber)));
    }

    // --- Test 5: ugyldige tegn i telefonnummer ---

    [Fact]
    public void PhoneWithLetters_ShouldFailValidation()
    {
        var model = CreateProfile(phoneNumber: "1234ABCD");

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(nameof(ProfileViewModel.PhoneNumber)));
    }

    // --- Test 6: telefonnummer er valgfritt ---

    [Fact]
    public void ProfileWithoutPhone_ShouldPassValidation()
    {
        var model = CreateProfile(phoneNumber: null);

        var results = Validate(model);

        Assert.Empty(results);
    }

    // --- Test 7: gyldige landskoder og numre ---

    [Theory]
    [InlineData("+47", "12345678")]
    [InlineData("+45", "12345678")]
    [InlineData("+46", "123456789")]
    public void ValidInternationalPhone_ShouldPassValidation(string countryCode, string phoneNumber)
    {
        var model = CreateProfile(countryCode: countryCode, phoneNumber: phoneNumber);

        var results = Validate(model);

        Assert.Empty(results);
    }
}
