using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Matching;
using FirstWebAppInDocker.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FirstWebAppInDocker.Tests;

/// <summary>
/// Tester avstandsberegning, matching og statusflyten for forespørsler.
/// </summary>
public class MatchingTests
{
    // Koordinater brukt i testene.
    private const double GrimstadLat = 58.3405, GrimstadLon = 8.5934;
    private const double ArendalLat = 58.4615, ArendalLon = 8.7725;
    private const double KristiansandLat = 58.1467, KristiansandLon = 7.9956;
    private const double OsloLat = 59.9139, OsloLon = 10.7522;


    // ========================================
    // HJELPERE
    // ========================================

    private static ApplicationDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new ApplicationDbContext(options);
    }


    private static Resource AddResource(
        ApplicationDbContext database,
        ResourceType type,
        double latitude,
        double longitude,
        string ownerUserId = "owner-1",
        ResourceStatus status = ResourceStatus.Available,
        string name = "Testressurs")
    {
        var resource = new Resource
        {
            ResourceType = type,
            ResourceName = name,
            Description = "Beskrivelse",
            Quantity = 1,
            Latitude = latitude,
            Longitude = longitude,
            OwnerUserId = ownerUserId,
            OwnerEmail = ownerUserId + "@example.com",
            Status = status
        };

        database.Resources.Add(resource);
        database.SaveChanges();

        return resource;
    }


    private static Need AddNeed(
        ApplicationDbContext database,
        ResourceType type,
        double latitude,
        double longitude,
        NeedStatus status = NeedStatus.New)
    {
        var need = new Need
        {
            ResourceType = type,
            Priority = NeedPriority.Acute,
            Quantity = 1,
            Description = "Testbehov",
            Latitude = latitude,
            Longitude = longitude,
            OwnerEmail = "actor@example.com",
            Status = status
        };

        database.Needs.Add(need);
        database.SaveChanges();

        return need;
    }


    private sealed class FakeEmailSender : IPortalEmailSender
    {
        public List<string> SentTo { get; } = new();

        public Task SendAsync(string toEmail, string subject, string body)
        {
            SentTo.Add(toEmail);
            return Task.CompletedTask;
        }
    }


    private sealed class FakeSmsSender : ISmsSender
    {
        public List<string> SentTo { get; } = new();

        public Task SendAsync(string phoneNumber, string message)
        {
            SentTo.Add(phoneNumber);
            return Task.CompletedTask;
        }
    }


    private static AssignmentService CreateAssignmentService(
        ApplicationDbContext database,
        FakeEmailSender? email = null)
    {
        var notifications = new NotificationService(
            database,
            email ?? new FakeEmailSender(),
            new FakeSmsSender(),
            new ConfigurationBuilder().Build(),
            NullLogger<NotificationService>.Instance);

        return new AssignmentService(database, notifications);
    }


    // ========================================
    // AVSTAND
    // ========================================

    [Fact]
    public void Haversine_SamePoint_IsZero()
    {
        double distance = GeoDistance.HaversineKm(GrimstadLat, GrimstadLon, GrimstadLat, GrimstadLon);

        Assert.Equal(0, distance, precision: 6);
    }


    [Fact]
    public void Haversine_GrimstadToArendal_IsAbout17Km()
    {
        double distance = GeoDistance.HaversineKm(GrimstadLat, GrimstadLon, ArendalLat, ArendalLon);

        // Luftlinje Grimstad–Arendal er ca. 17 km.
        Assert.InRange(distance, 15, 19);
    }


    [Fact]
    public void Haversine_IsSymmetric()
    {
        double there = GeoDistance.HaversineKm(GrimstadLat, GrimstadLon, OsloLat, OsloLon);
        double back = GeoDistance.HaversineKm(OsloLat, OsloLon, GrimstadLat, GrimstadLon);

        Assert.Equal(there, back, precision: 6);
    }


    // ========================================
    // MATCHING
    // ========================================

    [Fact]
    public async Task FindNearbyResources_ReturnsNearestFirst()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, ArendalLat, ArendalLon, name: "Arendal");
        AddResource(database, ResourceType.Boat, GrimstadLat + 0.01, GrimstadLon, name: "Grimstad");
        AddResource(database, ResourceType.Boat, KristiansandLat, KristiansandLon, name: "Kristiansand");

        var service = new ResourceMatchingService(database);

        var result = await service.FindNearbyResourcesAsync(
            GrimstadLat, GrimstadLon, radiusKm: 100, types: new[] { ResourceType.Boat });

        Assert.Equal(new[] { "Grimstad", "Arendal", "Kristiansand" }, result.Select(r => r.ResourceName));
        Assert.True(result[0].DistanceKm < result[1].DistanceKm);
    }


    [Fact]
    public async Task FindNearbyResources_IgnoresOtherTypesUnavailableAndFarAway()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, name: "Riktig");
        AddResource(database, ResourceType.Generator, GrimstadLat, GrimstadLon, name: "Feil type");
        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, status: ResourceStatus.Unavailable, name: "Utilgjengelig");
        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, status: ResourceStatus.Busy, name: "Opptatt");
        AddResource(database, ResourceType.Boat, OsloLat, OsloLon, name: "For langt unna");

        var service = new ResourceMatchingService(database);

        var result = await service.FindNearbyResourcesAsync(
            GrimstadLat, GrimstadLon, radiusKm: 50, types: new[] { ResourceType.Boat });

        Assert.Single(result);
        Assert.Equal("Riktig", result[0].ResourceName);
    }


    [Fact]
    public async Task FindNearbyResources_ExcludesResourcesAlreadyRequestedForNeed()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);
        Resource requested = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, name: "Spurt");
        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, name: "Ledig");

        database.NeedAssignments.Add(new NeedAssignment
        {
            NeedId = need.Id,
            ResourceId = requested.Id,
            RequestedByUserId = "actor",
            Status = AssignmentStatus.Requested
        });
        database.SaveChanges();

        var service = new ResourceMatchingService(database);

        var result = await service.FindNearbyResourcesAsync(
            GrimstadLat, GrimstadLon, types: new[] { ResourceType.Boat }, excludeRequestedForNeedId: need.Id);

        Assert.Single(result);
        Assert.Equal("Ledig", result[0].ResourceName);
    }


    [Fact]
    public async Task FindNeedsForProvider_OnlyReturnsNeedsMatchingOwnResourceTypes()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "me");
        AddResource(database, ResourceType.Generator, GrimstadLat, GrimstadLon, ownerUserId: "someone-else");

        AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);
        AddNeed(database, ResourceType.Generator, GrimstadLat, GrimstadLon);
        AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon, status: NeedStatus.Resolved);

        var service = new ResourceMatchingService(database);

        var result = await service.FindNeedsForProviderAsync("me");

        Assert.Single(result);
        Assert.Equal(ResourceType.Boat, result[0].ResourceType);
    }


    // ========================================
    // STATUSFLYT FOR FORESPØRSLER
    // ========================================

    [Fact]
    public async Task Request_SetsNeedUnderReview_AndNotifiesOwner()
    {
        using var database = CreateDatabase();

        database.Users.Add(new ApplicationUser { Id = "owner-1", Email = "owner@example.com", UserName = "owner@example.com" });
        database.SaveChanges();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);
        Resource resource = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        var email = new FakeEmailSender();
        var service = CreateAssignmentService(database, email);

        ServiceResult result = await service.RequestAsync(need.Id, resource.Id, "actor", "Trenger båt", false);

        Assert.True(result.Success);
        Assert.Equal(NeedStatus.UnderReview, database.Needs.Single().Status);
        Assert.Single(database.NeedAssignments);
        Assert.Single(database.Notifications.Where(n => n.UserId == "owner-1"));
        Assert.Contains("owner@example.com", email.SentTo);
    }


    [Fact]
    public async Task Request_SameResourceTwice_IsRejected()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);
        Resource resource = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        var service = CreateAssignmentService(database);

        await service.RequestAsync(need.Id, resource.Id, "actor", null, false);
        ServiceResult second = await service.RequestAsync(need.Id, resource.Id, "actor", null, false);

        Assert.False(second.Success);
        Assert.Single(database.NeedAssignments);
    }


    [Fact]
    public async Task Respond_Accept_AssignsNeedAndMarksResourceBusy()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);
        Resource resource = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "owner-1");

        var service = CreateAssignmentService(database);
        await service.RequestAsync(need.Id, resource.Id, "actor", null, false);

        int assignmentId = database.NeedAssignments.Single().Id;

        ServiceResult result = await service.RespondAsync(assignmentId, "owner-1", accept: true);

        Assert.True(result.Success);
        Assert.Equal(AssignmentStatus.Confirmed, database.NeedAssignments.Single().Status);
        Assert.Equal(NeedStatus.Assigned, database.Needs.Single().Status);
        Assert.Equal(ResourceStatus.Busy, database.Resources.Single().Status);
    }


    [Fact]
    public async Task Respond_Decline_SetsNeedBackToNew()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);
        Resource resource = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "owner-1");

        var service = CreateAssignmentService(database);
        await service.RequestAsync(need.Id, resource.Id, "actor", null, false);

        int assignmentId = database.NeedAssignments.Single().Id;

        await service.RespondAsync(assignmentId, "owner-1", accept: false);

        Assert.Equal(AssignmentStatus.Declined, database.NeedAssignments.Single().Status);
        Assert.Equal(NeedStatus.New, database.Needs.Single().Status);
        Assert.Equal(ResourceStatus.Available, database.Resources.Single().Status);
    }


    [Fact]
    public async Task Respond_ByOtherUser_IsRejected()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);
        Resource resource = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "owner-1");

        var service = CreateAssignmentService(database);
        await service.RequestAsync(need.Id, resource.Id, "actor", null, false);

        int assignmentId = database.NeedAssignments.Single().Id;

        ServiceResult result = await service.RespondAsync(assignmentId, "intruder", accept: true);

        Assert.False(result.Success);
        Assert.Equal(AssignmentStatus.Requested, database.NeedAssignments.Single().Status);
    }


    [Fact]
    public async Task ResolveNeed_FreesConfirmedResource_AndCancelsOpenRequests()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);
        Resource confirmed = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "owner-1");
        Resource pending = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "owner-2");

        var service = CreateAssignmentService(database);
        await service.RequestAsync(need.Id, confirmed.Id, "actor", null, false);
        await service.RequestAsync(need.Id, pending.Id, "actor", null, false);

        int confirmedAssignmentId =
            database.NeedAssignments.Single(a => a.ResourceId == confirmed.Id).Id;

        await service.RespondAsync(confirmedAssignmentId, "owner-1", accept: true);

        ServiceResult result = await service.ResolveNeedAsync(need.Id);

        Assert.True(result.Success);
        Assert.Equal(NeedStatus.Resolved, database.Needs.Single().Status);
        Assert.Equal(ResourceStatus.Available, database.Resources.Single(r => r.Id == confirmed.Id).Status);
        Assert.Equal(AssignmentStatus.Cancelled, database.NeedAssignments.Single(a => a.ResourceId == pending.Id).Status);
    }
}
