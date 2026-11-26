/*
    MatchingTests.cs – automatiske tester

    Hva filen gjør: Tester matchingen (avstand, radius, type, sortering, grensetilfeller) og
    forespørselsflyten. Bruker xUnit, en database i minnet (EF Core InMemory) og Moq, så testene
    kjører raskt uten Docker.

    Oppgavekrav: 6, 15
*/

using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services.Matching;
using FirstWebAppInDocker.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FirstWebAppInDocker.Tests;

/*
    Tester geografiske avstandsberegninger, matching mellom
    behov og ressurser, samt statusflyten for forespørsler.

    Testene bruker EF Core InMemory for å kontrollere
    applikasjonslogikken uten en ekstern database.

    Matchingservicen bruker det samme repository-interfacet
    som i produksjonskoden.
*/
public class MatchingTests
{
    #region Testkoordinater

    private const double GrimstadLat = 58.3405;
    private const double GrimstadLon = 8.5934;

    private const double ArendalLat = 58.4615;
    private const double ArendalLon = 8.7725;

    private const double KristiansandLat = 58.1467;
    private const double KristiansandLon = 7.9956;

    private const double OsloLat = 59.9139;
    private const double OsloLon = 10.7522;

    #endregion

    #region Testoppsett

    /*
        Oppretter en isolert InMemory-database for hver test.

        Returnerer: En ny ApplicationDbContext.
    */
    private static ApplicationDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new ApplicationDbContext(options);
    }

    /*
        Oppretter matchingservicen med et EF Core-repository.

        database: Testens databasekontekst.
        Returnerer: En konfigurert matchingservice.
    */
    private static ResourceMatchingService CreateMatchingService(ApplicationDbContext database)
    {
        var repository = new EfResourceMatchingRepository(database);

        return new ResourceMatchingService(repository);
    }

    /*
        Oppretter og lagrer en testressurs.

        database: Testens database.
        type: Ressurstype.
        latitude: Breddegrad.
        longitude: Lengdegrad.
        ownerUserId: Identifikator til eieren.
        status: Ressursens tilgjengelighetsstatus.
        name: Ressursens navn.
        Returnerer: Den lagrede ressursen.
    */
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
            Position = new Coordinate(latitude, longitude),
            OwnerUserId = ownerUserId,
            OwnerEmail = ownerUserId + "@example.com",
            Status = status
        };

        database.Resources.Add(resource);
        database.SaveChanges();

        return resource;
    }

    /*
        Oppretter og lagrer et testbehov.

        database: Testens database.
        type: Etterspurt ressurstype.
        latitude: Breddegrad.
        longitude: Lengdegrad.
        status: Behovets status.
        priority: Behovets prioritet.
        Returnerer: Det lagrede behovet.
    */
    private static Need AddNeed(
        ApplicationDbContext database,
        ResourceType type,
        double latitude,
        double longitude,
        NeedStatus status = NeedStatus.New,
        NeedPriority priority = NeedPriority.Acute)
    {
        var need = new Need
        {
            ResourceType = type,
            Priority = priority,
            Quantity = 1,
            Description = "Testbehov",
            Position = new Coordinate(latitude, longitude),
            OwnerEmail = "actor@example.com",
            Status = status
        };

        database.Needs.Add(need);
        database.SaveChanges();

        return need;
    }

    /*
        Testimplementasjon som registrerer e-postmottakere
        uten å sende faktiske e-poster.
    */
    private sealed class FakeEmailSender : IPortalEmailSender
    {
        // E-postadressene som har mottatt testmeldinger.
        public List<string> SentTo { get; } = new();

        public Task SendAsync(string toEmail, string subject, string body)
        {
            SentTo.Add(toEmail);
            return Task.CompletedTask;
        }
    }

    /*
        Testimplementasjon som registrerer SMS-mottakere
        uten å sende faktiske tekstmeldinger.
    */
    private sealed class FakeSmsSender : ISmsSender
    {
        // Telefonnummer som har mottatt testmeldinger.
        public List<string> SentTo { get; } = new();

        public Task SendAsync(string phoneNumber, string message)
        {
            SentTo.Add(phoneNumber);
            return Task.CompletedTask;
        }
    }

    /*
        Oppretter AssignmentService med testimplementasjoner
        for e-post og SMS.

        database: Testens database.
        email: Valgfri testimplementasjon for e-post.
        Returnerer: En konfigurert AssignmentService.
    */
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

    #endregion

    #region Koordinater og avstandsberegning

    /*
        Kontrollerer at Coordinate oppdaterer de eksisterende
        koordinatfeltene i både Resource og Need.
    */
    [Fact]
    public void Coordinate_PositionUpdatesLatitudeAndLongitude()
    {
        var resource = new Resource
        {
            Position = new Coordinate(GrimstadLat, GrimstadLon)
        };

        var need = new Need
        {
            Position = new Coordinate(ArendalLat, ArendalLon)
        };

        Assert.Equal(GrimstadLat, resource.Latitude);
        Assert.Equal(GrimstadLon, resource.Longitude);

        Assert.Equal(ArendalLat, need.Latitude);
        Assert.Equal(ArendalLon, need.Longitude);

        Assert.Equal(new Coordinate(GrimstadLat, GrimstadLon), resource.Position);

        Assert.Equal(new Coordinate(ArendalLat, ArendalLon), need.Position);
    }

    // Kontrollerer at avstanden fra et punkt til seg selv er null.
    [Fact]
    public void Haversine_SamePoint_IsZero()
    {
        double distance = GeoDistance.HaversineKm(
            GrimstadLat,
            GrimstadLon,
            GrimstadLat,
            GrimstadLon);

        Assert.Equal(0, distance, precision: 6);
    }

    /*
        Kontrollerer en kjent omtrentlig luftlinjeavstand
        mellom Grimstad og Arendal.
    */
    [Fact]
    public void Haversine_GrimstadToArendal_IsAbout17Km()
    {
        double distance = GeoDistance.HaversineKm(GrimstadLat, GrimstadLon, ArendalLat, ArendalLon);

        Assert.InRange(distance, 15, 19);
    }

    // Kontrollerer at avstanden er lik i begge retninger.
    [Fact]
    public void Haversine_IsSymmetric()
    {
        double there = GeoDistance.HaversineKm(GrimstadLat, GrimstadLon, OsloLat, OsloLon);

        double back = GeoDistance.HaversineKm(OsloLat, OsloLon, GrimstadLat, GrimstadLon);

        Assert.Equal(there, back, precision: 6);
    }

    #endregion

    #region Matching av ressurser

    /*
        Kontrollerer at ressurser returneres sortert
        fra kortest til lengst geografisk avstand.
    */
    [Fact]
    public async Task FindNearbyResources_ReturnsNearestFirst()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, ArendalLat, ArendalLon, name: "Arendal");

        AddResource(database, ResourceType.Boat, GrimstadLat + 0.01, GrimstadLon, name: "Grimstad");

        AddResource(
            database, ResourceType.Boat,
            KristiansandLat, KristiansandLon,
            name: "Kristiansand");

        var service = CreateMatchingService(database);

        var result = await service.FindNearbyResourcesAsync(
            GrimstadLat,
            GrimstadLon,
            radiusKm: 100,
            types: new[] { ResourceType.Boat });

        Assert.Equal(
            new[] { "Grimstad", "Arendal", "Kristiansand" },
            result.Select(resource => resource.ResourceName));

        Assert.True(result[0].DistanceKm < result[1].DistanceKm);
    }

    /*
        Kontrollerer at feil ressurstype, utilgjengelige
        ressurser og ressurser utenfor radius ekskluderes.
    */
    [Fact]
    public async Task FindNearbyResources_IgnoresOtherTypesUnavailableAndFarAway()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, name: "Riktig");

        AddResource(database, ResourceType.Generator, GrimstadLat, GrimstadLon, name: "Feil type");

        AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            status: ResourceStatus.Unavailable,
            name: "Utilgjengelig");

        AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            status: ResourceStatus.Busy,
            name: "Opptatt");

        AddResource(database, ResourceType.Boat, OsloLat, OsloLon, name: "For langt unna");

        var service = CreateMatchingService(database);

        var result = await service.FindNearbyResourcesAsync(
            GrimstadLat,
            GrimstadLon,
            radiusKm: 50,
            types: new[] { ResourceType.Boat });

        Assert.Single(result);
        Assert.Equal("Riktig", result[0].ResourceName);
    }

    /*
        Kontrollerer at alle tilgjengelige ressurstyper
        inkluderes når det ikke er valgt et typefilter.
    */
    [Fact]
    public async Task FindNearbyResources_WithoutTypeFilter_ReturnsAllTypes()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        AddResource(database, ResourceType.Generator, GrimstadLat, GrimstadLon);

        AddResource(database, ResourceType.Drone, GrimstadLat, GrimstadLon);

        var service = CreateMatchingService(database);

        var result = await service.FindNearbyResourcesAsync(GrimstadLat, GrimstadLon, types: null);

        Assert.Equal(3, result.Count);

        Assert.Contains(result, resource => resource.ResourceType == ResourceType.Boat);

        Assert.Contains(result, resource => resource.ResourceType == ResourceType.Generator);

        Assert.Contains(result, resource => resource.ResourceType == ResourceType.Drone);
    }

    /*
        Kontrollerer at maksgrensen for antall resultater
        respekteres og at lik avstand gir stabil sortering.
    */
    [Fact]
    public async Task FindNearbyResources_RespectsMaxResults()
    {
        using var database = CreateDatabase();

        Resource first = AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            name: "Første");

        Resource second = AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            name: "Andre");

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, name: "Tredje");

        var service = CreateMatchingService(database);

        var result = await service.FindNearbyResourcesAsync(
            GrimstadLat,
            GrimstadLon,
            maxResults: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(first.Id, result[0].ResourceId);
        Assert.Equal(second.Id, result[1].ResourceId);
    }

    /*
        Kontrollerer at filtreringen bruker nøyaktig avstand
        og ikke den avrundede visningsavstanden.
    */
    [Fact]
    public async Task FindNearbyResources_ExcludesPointJustOutsideRadius()
    {
        using var database = CreateDatabase();

        /*
            Posisjonen ligger diagonalt fra Grimstad.
            Avstanden er litt over 50 km, men avrundes
            til omtrent 50,0 km med én desimal.
        */
        double outsideLatitude = GrimstadLat + 0.30;
        double outsideLongitude = GrimstadLon + 0.6415;

        double actualDistance = GeoDistance.HaversineKm(
            GrimstadLat,
            GrimstadLon,
            outsideLatitude,
            outsideLongitude);

        Assert.True(actualDistance > 50);
        Assert.Equal(50.0, Math.Round(actualDistance, 1));

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, name: "Innenfor");

        AddResource(
            database, ResourceType.Boat,
            outsideLatitude, outsideLongitude,
            name: "Utenfor");

        var service = CreateMatchingService(database);

        var result = await service.FindNearbyResourcesAsync(GrimstadLat, GrimstadLon, radiusKm: 50);

        Assert.Single(result);
        Assert.Equal("Innenfor", result[0].ResourceName);
    }

    /*
        Kontrollerer at en ressurs som allerede er forespurt
        for et behov, ikke foreslås på nytt for samme behov.
    */
    [Fact]
    public async Task FindNearbyResources_ExcludesResourcesAlreadyRequestedForNeed()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        Resource requested = AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            name: "Spurt");

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, name: "Ledig");

        database.NeedAssignments.Add(new NeedAssignment
        {
            NeedId = need.Id,
            ResourceId = requested.Id,
            RequestedByUserId = "actor",
            Status = AssignmentStatus.Requested
        });

        database.SaveChanges();

        var service = CreateMatchingService(database);

        var result = await service.FindNearbyResourcesAsync(
            GrimstadLat,
            GrimstadLon,
            types: new[] { ResourceType.Boat },
            excludeRequestedForNeedId: need.Id);

        Assert.Single(result);
        Assert.Equal("Ledig", result[0].ResourceName);
    }

    /*
        Kontrollerer at aktive forespørsler blokkerer ny matching,
        mens avslåtte og kansellerte forespørsler ikke gjør det.

        status: Forespørselens status.
        shouldBeIncluded:
        Om ressursen skal kunne matches på nytt.
    */
    [Theory]
    [InlineData(AssignmentStatus.Requested, false)]
    [InlineData(AssignmentStatus.Confirmed, false)]
    [InlineData(AssignmentStatus.Declined, true)]
    [InlineData(AssignmentStatus.Cancelled, true)]
    public async Task FindNearbyResources_FiltersAssignmentsByStatus(
        AssignmentStatus status,
        bool shouldBeIncluded)
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        Resource resource = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        /*
            Ressursen holdes tilgjengelig for å isolere
            filtreringen på AssignmentStatus.
        */
        database.NeedAssignments.Add(new NeedAssignment
        {
            NeedId = need.Id,
            ResourceId = resource.Id,
            RequestedByUserId = "actor",
            Status = status
        });

        database.SaveChanges();

        var service = CreateMatchingService(database);

        var result = await service.FindNearbyResourcesAsync(
            GrimstadLat,
            GrimstadLon,
            excludeRequestedForNeedId: need.Id);

        Assert.Equal(shouldBeIncluded, result.Any(candidate =>
            candidate.ResourceId == resource.Id));
    }

    #endregion

    #region Matching av behov for ressursleverandør

    /*
        Kontrollerer at en leverandør bare ser åpne behov
        som samsvarer med egne ressurstyper.
    */
    [Fact]
    public async Task FindNeedsForProvider_OnlyReturnsNeedsMatchingOwnResourceTypes()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "me");

        AddResource(
            database, ResourceType.Generator,
            GrimstadLat, GrimstadLon,
            ownerUserId: "someone-else");

        AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        AddNeed(database, ResourceType.Generator, GrimstadLat, GrimstadLon);

        AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon, status: NeedStatus.Resolved);

        var service = CreateMatchingService(database);

        var result = await service.FindNeedsForProviderAsync("me");

        Assert.Single(result);
        Assert.Equal(ResourceType.Boat, result[0].ResourceType);
    }

    /*
        Kontrollerer at akutte behov prioriteres foran
        planlagte behov, selv om de ligger lenger unna.
    */
    [Fact]
    public async Task FindNeedsForProvider_PrioritizesAcuteNeeds()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "me");

        Need planned = AddNeed(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            priority: NeedPriority.Planned);

        Need acute = AddNeed(
            database, ResourceType.Boat,
            ArendalLat, ArendalLon,
            priority: NeedPriority.Acute);

        var service = CreateMatchingService(database);

        var result = await service.FindNeedsForProviderAsync("me", radiusKm: 100);

        Assert.Equal(2, result.Count);
        Assert.Equal(acute.Id, result[0].NeedId);
        Assert.Equal(planned.Id, result[1].NeedId);

        // Prioritet skal overstyre avstand.
        Assert.True(result[0].DistanceKm > result[1].DistanceKm);
    }

    /*
        Kontrollerer at et behov matches med leverandørens
        nærmeste tilgjengelige ressurs av riktig type.
    */
    [Fact]
    public async Task FindNeedsForProvider_UsesClosestMatchingResource()
    {
        using var database = CreateDatabase();

        AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            ownerUserId: "me",
            name: "Båt i Grimstad");

        Resource closest = AddResource(
            database, ResourceType.Boat,
            ArendalLat, ArendalLon,
            ownerUserId: "me",
            name: "Båt i Arendal");

        Need need = AddNeed(database, ResourceType.Boat, ArendalLat + 0.01, ArendalLon);

        var service = CreateMatchingService(database);

        var result = await service.FindNeedsForProviderAsync("me", radiusKm: 100);

        Assert.Single(result);

        Assert.Equal(need.Id, result[0].NeedId);
        Assert.Equal(closest.Id, result[0].MatchingResourceId);
        Assert.Equal("Båt i Arendal", result[0].MatchingResourceName);
    }

    /*
        Kontrollerer at behov utenfor valgt radius
        ikke returneres til ressursleverandøren.
    */
    [Fact]
    public async Task FindNeedsForProvider_ExcludesNeedsOutsideRadius()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "me");

        AddNeed(database, ResourceType.Boat, OsloLat, OsloLon);

        var service = CreateMatchingService(database);

        var result = await service.FindNeedsForProviderAsync("me", radiusKm: 50);

        Assert.Empty(result);
    }

    /*
        Kontrollerer at behov som vurderes fortsatt
        kan vises, mens tildelte og løste behov utelates.
    */
    [Fact]
    public async Task FindNeedsForProvider_OnlyIncludesOpenStatuses()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "me");

        Need newNeed = AddNeed(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            status: NeedStatus.New);

        Need underReview = AddNeed(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            status: NeedStatus.UnderReview);

        AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon, status: NeedStatus.Assigned);

        AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon, status: NeedStatus.Resolved);

        var service = CreateMatchingService(database);

        var result = await service.FindNeedsForProviderAsync("me");

        Assert.Equal(2, result.Count);

        Assert.Contains(result, candidate => candidate.NeedId == newNeed.Id);

        Assert.Contains(result, candidate => candidate.NeedId == underReview.Id);
    }

    /*
        Kontrollerer at en manglende brukeridentifikator
        ikke gir tilgang til registrerte behov.
    */
    [Fact]
    public async Task FindNeedsForProvider_EmptyUserId_ReturnsNoResults()
    {
        using var database = CreateDatabase();

        AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon, ownerUserId: "me");

        AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        var service = CreateMatchingService(database);

        var result = await service.FindNeedsForProviderAsync(string.Empty);

        Assert.Empty(result);
    }

    #endregion

    #region Statusflyt for forespørsler

    /*
        Kontrollerer at forespørselen opprettes,
        behovet settes til vurdering og eieren varsles.
    */
    [Fact]
    public async Task Request_SetsNeedUnderReview_AndNotifiesOwner()
    {
        using var database = CreateDatabase();

        database.Users.Add(new ApplicationUser
        {
            Id = "owner-1",
            Email = "owner@example.com",
            UserName = "owner@example.com"
        });

        database.SaveChanges();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        Resource resource = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        var email = new FakeEmailSender();
        var service = CreateAssignmentService(database, email);

        ServiceResult result = await service.RequestAsync(
            need.Id,
            resource.Id,
            "actor",
            "Trenger båt",
            false);

        Assert.True(result.Success);

        Assert.Equal(NeedStatus.UnderReview, database.Needs.Single().Status);

        Assert.Single(database.NeedAssignments);

        Assert.Single(
            database.Notifications.Where(notification => notification.UserId == "owner-1"));

        Assert.Contains("owner@example.com", email.SentTo);
    }

    /*
        Kontrollerer at samme ressurs ikke kan få
        to aktive forespørsler for det samme behovet.
    */
    [Fact]
    public async Task Request_SameResourceTwice_IsRejected()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        Resource resource = AddResource(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        var service = CreateAssignmentService(database);

        await service.RequestAsync(need.Id, resource.Id, "actor", null, false);

        ServiceResult second = await service.RequestAsync(
            need.Id, resource.Id, "actor", null, false);

        Assert.False(second.Success);
        Assert.Single(database.NeedAssignments);
    }

    /*
        Kontrollerer at aksept av en forespørsel tildeler
        behovet og markerer ressursen som opptatt.
    */
    [Fact]
    public async Task Respond_Accept_AssignsNeedAndMarksResourceBusy()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        Resource resource = AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            ownerUserId: "owner-1");

        var service = CreateAssignmentService(database);

        await service.RequestAsync(need.Id, resource.Id, "actor", null, false);

        int assignmentId = database.NeedAssignments.Single().Id;

        ServiceResult result = await service.RespondAsync(assignmentId, "owner-1", accept: true);

        Assert.True(result.Success);

        Assert.Equal(AssignmentStatus.Confirmed, database.NeedAssignments.Single().Status);

        Assert.Equal(NeedStatus.Assigned, database.Needs.Single().Status);

        Assert.Equal(ResourceStatus.Busy, database.Resources.Single().Status);
    }

    /*
        Kontrollerer at avslag setter behovet tilbake
        til Ny og lar ressursen være tilgjengelig.
    */
    [Fact]
    public async Task Respond_Decline_SetsNeedBackToNew()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        Resource resource = AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            ownerUserId: "owner-1");

        var service = CreateAssignmentService(database);

        await service.RequestAsync(need.Id, resource.Id, "actor", null, false);

        int assignmentId = database.NeedAssignments.Single().Id;

        await service.RespondAsync(assignmentId, "owner-1", accept: false);

        Assert.Equal(AssignmentStatus.Declined, database.NeedAssignments.Single().Status);

        Assert.Equal(NeedStatus.New, database.Needs.Single().Status);

        Assert.Equal(ResourceStatus.Available, database.Resources.Single().Status);
    }

    /*
        Kontrollerer at en annen bruker ikke kan
        godta forespørselen til ressurseieren.
    */
    [Fact]
    public async Task Respond_ByOtherUser_IsRejected()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        Resource resource = AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            ownerUserId: "owner-1");

        var service = CreateAssignmentService(database);

        await service.RequestAsync(need.Id, resource.Id, "actor", null, false);

        int assignmentId = database.NeedAssignments.Single().Id;

        ServiceResult result = await service.RespondAsync(assignmentId, "intruder", accept: true);

        Assert.False(result.Success);

        Assert.Equal(AssignmentStatus.Requested, database.NeedAssignments.Single().Status);
    }

    /*
        Kontrollerer at løsning av behovet frigjør
        bekreftede ressurser og kansellerer åpne forespørsler.
    */
    [Fact]
    public async Task ResolveNeed_FreesConfirmedResource_AndCancelsOpenRequests()
    {
        using var database = CreateDatabase();

        Need need = AddNeed(database, ResourceType.Boat, GrimstadLat, GrimstadLon);

        Resource confirmed = AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            ownerUserId: "owner-1");

        Resource pending = AddResource(
            database, ResourceType.Boat,
            GrimstadLat, GrimstadLon,
            ownerUserId: "owner-2");

        var service = CreateAssignmentService(database);

        await service.RequestAsync(need.Id, confirmed.Id, "actor", null, false);

        await service.RequestAsync(need.Id, pending.Id, "actor", null, false);

        int confirmedAssignmentId =
            database.NeedAssignments.Single(assignment => assignment.ResourceId == confirmed.Id).Id;

        await service.RespondAsync(confirmedAssignmentId, "owner-1", accept: true);

        ServiceResult result = await service.ResolveNeedAsync(need.Id);

        Assert.True(result.Success);

        Assert.Equal(NeedStatus.Resolved, database.Needs.Single().Status);

        Assert.Equal(
            ResourceStatus.Available,
            database.Resources.Single(resource => resource.Id == confirmed.Id).Status);

        Assert.Equal(
            AssignmentStatus.Cancelled,
            database.NeedAssignments.Single(
                assignment => assignment.ResourceId == pending.Id).Status);
    }

    #endregion
}
