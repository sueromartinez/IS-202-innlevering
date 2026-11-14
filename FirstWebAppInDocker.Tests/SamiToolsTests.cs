using System.Text.Json;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Ai;
using FirstWebAppInDocker.Services.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FirstWebAppInDocker.Tests;

/// <summary>
/// Tester at Samis verktøy respekterer roller, ikke lekker
/// kontaktinformasjon, og at forslag valideres mot databasen.
/// Testene kaller verktøyene direkte, uten språkmodell.
/// </summary>
public class SamiToolsTests
{
    private static ApplicationDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new ApplicationDbContext(options);
    }


    /// <summary>
    /// Falskt adresseoppslag, så testene ikke trenger nettverk.
    /// Kjenner bare "Storgata 5, Grimstad".
    /// </summary>
    private sealed class FakeAddressLookup : IAddressLookup
    {
        public Task<AddressResult?> LookupAsync(string text, CancellationToken cancellationToken)
        {
            AddressResult? result =
                text.Contains("Storgata 5", StringComparison.OrdinalIgnoreCase)
                    ? new AddressResult("Storgata 5", "4876, GRIMSTAD", "Grimstad", 58.3405, 8.5934)
                    : null;

            return Task.FromResult(result);
        }
    }


    private static SamiTools CreateTools(ApplicationDbContext database)
    {
        // Geokoderen brukes ikke i disse testene.
        var geocoder = new NominatimGeocoder(new HttpClient(), NullLogger<NominatimGeocoder>.Instance);

        return new SamiTools(database, new ResourceMatchingService(database), geocoder, new FakeAddressLookup());
    }


    private static ChatToolCall Call(string name, string argumentsJson = "{}")
    {
        return new ChatToolCall
        {
            Id = "test",
            Name = name,
            Arguments = JsonDocument.Parse(argumentsJson).RootElement.Clone()
        };
    }


    private static (Need Need, Resource Resource) SeedPair(ApplicationDbContext database)
    {
        var need = new Need
        {
            ResourceType = ResourceType.Boat,
            Priority = NeedPriority.Acute,
            Description = "Evakuering",
            Latitude = 58.34,
            Longitude = 8.59,
            OwnerEmail = "actor@example.com"
        };

        var resource = new Resource
        {
            ResourceType = ResourceType.Boat,
            ResourceName = "Sjark",
            Description = "Båt",
            Latitude = 58.345,
            Longitude = 8.595,
            OwnerUserId = "owner-1",
            OwnerEmail = "owner@example.com",
            ContactEmail = "hemmelig@example.com",
            ContactPhoneCountryCode = "+47",
            ContactPhoneNumber = "99999999"
        };

        database.Needs.Add(need);
        database.Resources.Add(resource);
        database.SaveChanges();

        return (need, resource);
    }


    [Fact]
    public void Volunteer_DoesNotGetMatchingTools()
    {
        using var database = CreateDatabase();
        SamiTools tools = CreateTools(database);

        var names = tools.GetDefinitions(SamiRole.Volunteer).Select(tool => tool.Name).ToList();

        Assert.DoesNotContain("find_nearby_resources", names);
        Assert.DoesNotContain("suggest_match", names);
        Assert.DoesNotContain("get_statistics", names);
        Assert.Contains("get_registration_rules", names);
    }


    [Fact]
    public async Task Volunteer_CallingForbiddenTool_GetsError()
    {
        using var database = CreateDatabase();
        SeedPair(database);
        SamiTools tools = CreateTools(database);

        var context = new SamiContext { UserId = "owner-1", Role = SamiRole.Volunteer };

        string result = await tools.ExecuteAsync(
            Call("find_nearby_resources", """{ "latitude": 58.34, "longitude": 8.59 }"""),
            context,
            new SamiTurnOutput(),
            CancellationToken.None);

        Assert.Contains("ikke tilgjengelig", result);
        Assert.DoesNotContain("Sjark", result);
    }


    [Fact]
    public async Task FindNearbyResources_NeverReturnsContactInformation()
    {
        using var database = CreateDatabase();
        (Need need, _) = SeedPair(database);
        SamiTools tools = CreateTools(database);

        var context = new SamiContext { UserId = "actor", Role = SamiRole.PublicActor };

        string result = await tools.ExecuteAsync(
            Call("find_nearby_resources", $$"""{ "need_id": {{need.Id}} }"""),
            context,
            new SamiTurnOutput(),
            CancellationToken.None);

        Assert.Contains("Sjark", result);
        Assert.DoesNotContain("hemmelig@example.com", result);
        Assert.DoesNotContain("99999999", result);
    }


    [Fact]
    public async Task SuggestMatch_ValidPair_AddsSuggestionWithoutSavingAnything()
    {
        using var database = CreateDatabase();
        (Need need, Resource resource) = SeedPair(database);
        SamiTools tools = CreateTools(database);

        var output = new SamiTurnOutput();
        var context = new SamiContext { UserId = "actor", Role = SamiRole.PublicActor };

        string result = await tools.ExecuteAsync(
            Call("suggest_match", $$"""{ "need_id": {{need.Id}}, "resource_id": "{{resource.Id}}", "reason": "Nærmeste båt" }"""),
            context,
            output,
            CancellationToken.None);

        Assert.Contains("\"ok\":true", result);
        Assert.Single(output.Suggestions);
        Assert.Equal(resource.Id, output.Suggestions[0].ResourceId);
        Assert.Empty(database.NeedAssignments);
    }


    [Fact]
    public async Task SuggestMatch_UnknownResource_IsRejected()
    {
        using var database = CreateDatabase();
        (Need need, _) = SeedPair(database);
        SamiTools tools = CreateTools(database);

        var output = new SamiTurnOutput();
        var context = new SamiContext { UserId = "actor", Role = SamiRole.PublicActor };

        string result = await tools.ExecuteAsync(
            Call("suggest_match", $$"""{ "need_id": {{need.Id}}, "resource_id": 9999, "reason": "Finnes ikke" }"""),
            context,
            output,
            CancellationToken.None);

        Assert.Contains("\"ok\":false", result);
        Assert.Empty(output.Suggestions);
    }


    // ========================================
    // HJELPEARTIKLER
    // ========================================

    [Theory]
    [InlineData("Hvordan skrur jeg på dark mode?", "tema")]
    [InlineData("hvordan får jeg mørk modus", "tema")]
    [InlineData("Hei Sami, hvordan går jeg opp i nivå?", "niva")]
    [InlineData("jeg har glemt passordet", "glemt-passord")]
    [InlineData("hvor ser jeg varsler", "varsler")]
    public void HelpSearch_FindsRightArticle(string question, string expectedId)
    {
        var result = SamiHelpGuide.Search(question, SamiRole.Volunteer);

        Assert.NotEmpty(result);
        Assert.Equal(expectedId, result[0].Id);
    }


    [Fact]
    public void HelpSearch_DoesNotShowAdminArticlesToVolunteers()
    {
        var result = SamiHelpGuide.Search("deaktivere bruker endre rolle admin", SamiRole.Volunteer);

        Assert.DoesNotContain(result, article => article.Id == "admin-brukere");
    }


    [Fact]
    public async Task SearchHelpTool_ReturnsArticleText()
    {
        using var database = CreateDatabase();
        SamiTools tools = CreateTools(database);

        string result = await tools.ExecuteAsync(
            Call("search_help", """{ "query": "mørk modus" }"""),
            new SamiContext { UserId = "u1", Role = SamiRole.PublicActor },
            new SamiTurnOutput(),
            CancellationToken.None);

        Assert.Contains("måne", result);
    }


    // ========================================
    // NIVÅ
    // ========================================

    [Fact]
    public async Task GetMyLevel_TwoResources_IsSilver()
    {
        using var database = CreateDatabase();

        for (int i = 0; i < 2; i++)
        {
            database.Resources.Add(new Resource
            {
                ResourceType = ResourceType.Boat,
                ResourceName = "Båt " + i,
                Description = "Båt",
                Latitude = 58.3,
                Longitude = 8.5,
                OwnerUserId = "owner-1",
                OwnerEmail = "owner@example.com"
            });
        }

        database.SaveChanges();
        SamiTools tools = CreateTools(database);

        string result = await tools.ExecuteAsync(
            Call("get_my_level"),
            new SamiContext { UserId = "owner-1", Role = SamiRole.Volunteer },
            new SamiTurnOutput(),
            CancellationToken.None);

        Assert.Contains("\"level\":\"Sølv\"", result);
        Assert.Contains("\"points\":200", result);
    }


    // ========================================
    // REGISTRERE RESSURS VIA SAMI
    // ========================================

    private const string ValidDraftArguments = """
        {
          "resource_type": "Boat",
          "resource_name": "Sjark",
          "description": "Båt som kan frakte personer i grunt vann.",
          "quantity": 1,
          "availability_months": 6,
          "location": "Storgata 5, Grimstad"
        }
        """;


    [Fact]
    public async Task PrepareResource_ValidInput_CreatesDraftWithoutSaving()
    {
        using var database = CreateDatabase();
        SamiTools tools = CreateTools(database);
        var output = new SamiTurnOutput();

        string result = await tools.ExecuteAsync(
            Call("prepare_resource_registration", ValidDraftArguments),
            new SamiContext { UserId = "owner-1", Role = SamiRole.Volunteer },
            output,
            CancellationToken.None);

        Assert.Contains("\"ok\":true", result);
        Assert.Single(output.ResourceDrafts);

        SamiResourceDraft draft = output.ResourceDrafts[0];
        Assert.Equal("Boat", draft.ResourceType);
        Assert.Equal("Storgata 5", draft.StreetAddress);
        Assert.Equal(6, draft.AvailabilityMonths);
        Assert.Equal(58.3405, draft.Latitude, precision: 4);

        // Ingenting skal være lagret før brukeren trykker på knappen.
        Assert.Empty(database.Resources);
    }


    [Fact]
    public async Task PrepareResource_MissingFields_AsksForThem()
    {
        using var database = CreateDatabase();
        SamiTools tools = CreateTools(database);
        var output = new SamiTurnOutput();

        string result = await tools.ExecuteAsync(
            Call("prepare_resource_registration", """{ "resource_type": "Boat", "resource_name": "Sjark" }"""),
            new SamiContext { UserId = "owner-1", Role = SamiRole.Volunteer },
            output,
            CancellationToken.None);

        Assert.Contains("\"ok\":false", result);
        Assert.Contains("antall", result);
        Assert.Contains("adresse", result);
        Assert.Empty(output.ResourceDrafts);
    }


    [Fact]
    public async Task PrepareResource_InvalidPeriodAndPhone_UsesFormRules()
    {
        using var database = CreateDatabase();
        SamiTools tools = CreateTools(database);
        var output = new SamiTurnOutput();

        string result = await tools.ExecuteAsync(
            Call("prepare_resource_registration", """
                {
                  "resource_type": "Boat",
                  "resource_name": "Sjark",
                  "description": "Båt",
                  "quantity": 1,
                  "availability_months": 5,
                  "location": "Storgata 5, Grimstad",
                  "phone_number": "123"
                }
                """),
            new SamiContext { UserId = "owner-1", Role = SamiRole.Volunteer },
            output,
            CancellationToken.None);

        Assert.Contains("\"ok\":false", result);
        Assert.Contains("8 sifre", result);
        Assert.Empty(output.ResourceDrafts);
    }


    [Fact]
    public async Task PrepareResource_UnknownAddress_IsRejected()
    {
        using var database = CreateDatabase();
        SamiTools tools = CreateTools(database);
        var output = new SamiTurnOutput();

        string result = await tools.ExecuteAsync(
            Call("prepare_resource_registration", ValidDraftArguments.Replace("Storgata 5, Grimstad", "Finnesikke 99")),
            new SamiContext { UserId = "owner-1", Role = SamiRole.Volunteer },
            output,
            CancellationToken.None);

        Assert.Contains("Fant ikke stedet", result);
        Assert.Empty(output.ResourceDrafts);
    }


    [Fact]
    public async Task PrepareResource_NotAvailableForPublicActor()
    {
        using var database = CreateDatabase();
        SamiTools tools = CreateTools(database);
        var output = new SamiTurnOutput();

        string result = await tools.ExecuteAsync(
            Call("prepare_resource_registration", ValidDraftArguments),
            new SamiContext { UserId = "actor", Role = SamiRole.PublicActor },
            output,
            CancellationToken.None);

        Assert.Contains("ikke tilgjengelig", result);
        Assert.Empty(output.ResourceDrafts);
    }
}
