using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.ViewModels;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class ProfileViewModelTests
{
    // ========================================
    // HELPER - GYLDIG PROFIL
    // ========================================

    private static ProfileViewModel CreateProfile(
        string name = "Ola Nordmann",
        string email = "ola@example.com",
        string countryCode = "+47",
        string? phoneNumber = "12345678"
    )
    {
        return new ProfileViewModel
        {
            Name = name,
            Email = email,
            PhoneCountryCode = countryCode,
            PhoneNumber = phoneNumber
        };
    }


    // ========================================
    // HELPER - VALIDERING
    // ========================================

    private static List<ValidationResult> Validate(
        ProfileViewModel model
    )
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
    // TEST 1 - GYLDIG PROFIL
    // ========================================

    [Fact]
    public void ValidProfile_ShouldPassValidation()
    {
        var model = CreateProfile();

        var results = Validate(model);

        Assert.Empty(results);
    }


    // ========================================
    // TEST 2 - MANGLENDE NAVN
    // ========================================

    [Fact]
    public void MissingName_ShouldFailValidation()
    {
        var model = CreateProfile(
            name: ""
        );

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(ProfileViewModel.Name)
            )
        );
    }


    // ========================================
    // TEST 3 - UGYLDIG E-POST
    // ========================================

    [Fact]
    public void InvalidEmail_ShouldFailValidation()
    {
        var model = CreateProfile(
            email: "ugyldig-epost"
        );

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(ProfileViewModel.Email)
            )
        );
    }


    // ========================================
    // TEST 4 - UGYLDIG NORSK TELEFONNUMMER
    // ========================================

    [Fact]
    public void NorwegianPhoneWithSevenDigits_ShouldFailValidation()
    {
        var model = CreateProfile(
            countryCode: "+47",
            phoneNumber: "1234567"
        );

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(ProfileViewModel.PhoneNumber)
            )
        );
    }


    // ========================================
    // TEST 5 - UGYLDIGE TEGN I TELEFONNUMMER
    // ========================================

    [Fact]
    public void PhoneWithLetters_ShouldFailValidation()
    {
        var model = CreateProfile(
            phoneNumber: "1234ABCD"
        );

        var results = Validate(model);

        Assert.Contains(
            results,
            result => result.MemberNames.Contains(
                nameof(ProfileViewModel.PhoneNumber)
            )
        );
    }


    // ========================================
    // TEST 6 - TELEFONNUMMER ER VALGFRITT
    // ========================================

    [Fact]
    public void ProfileWithoutPhone_ShouldPassValidation()
    {
        var model = CreateProfile(
            phoneNumber: null
        );

        var results = Validate(model);

        Assert.Empty(results);
    }


    // ========================================
    // TEST 7 - GYLDIGE LANDSKODER OG NUMRE
    // ========================================

    [Theory]
    [InlineData("+47", "12345678")]
    [InlineData("+45", "12345678")]
    [InlineData("+46", "123456789")]
    public void ValidInternationalPhone_ShouldPassValidation(
        string countryCode,
        string phoneNumber
    )
    {
        var model = CreateProfile(
            countryCode: countryCode,
            phoneNumber: phoneNumber
        );

        var results = Validate(model);

        Assert.Empty(results);
    }
}