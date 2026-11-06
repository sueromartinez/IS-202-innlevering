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


    private static SamiTools CreateTools(ApplicationDbContext database)
    {
        // Geokoderen brukes ikke i disse testene.
        var geocoder = new NominatimGeocoder(new HttpClient(), NullLogger<NominatimGeocoder>.Instance);

        return new SamiTools(database, new ResourceMatchingService(database), geocoder);
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
            new List<SamiSuggestion>(),
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
            new List<SamiSuggestion>(),
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

        var suggestions = new List<SamiSuggestion>();
        var context = new SamiContext { UserId = "actor", Role = SamiRole.PublicActor };

        string result = await tools.ExecuteAsync(
            Call("suggest_match", $$"""{ "need_id": {{need.Id}}, "resource_id": "{{resource.Id}}", "reason": "Nærmeste båt" }"""),
            context,
            suggestions,
            CancellationToken.None);

        Assert.Contains("\"ok\":true", result);
        Assert.Single(suggestions);
        Assert.Equal(resource.Id, suggestions[0].ResourceId);
        Assert.Empty(database.NeedAssignments);
    }


    [Fact]
    public async Task SuggestMatch_UnknownResource_IsRejected()
    {
        using var database = CreateDatabase();
        (Need need, _) = SeedPair(database);
        SamiTools tools = CreateTools(database);

        var suggestions = new List<SamiSuggestion>();
        var context = new SamiContext { UserId = "actor", Role = SamiRole.PublicActor };

        string result = await tools.ExecuteAsync(
            Call("suggest_match", $$"""{ "need_id": {{need.Id}}, "resource_id": 9999, "reason": "Finnes ikke" }"""),
            context,
            suggestions,
            CancellationToken.None);

        Assert.Contains("\"ok\":false", result);
        Assert.Empty(suggestions);
    }
}
