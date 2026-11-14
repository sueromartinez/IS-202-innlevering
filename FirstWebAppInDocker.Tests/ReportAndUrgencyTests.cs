using System.Security.Claims;
using System.Text;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.Services;
using FirstWebAppInDocker.Services.Matching;
using FirstWebAppInDocker.Services.Reports;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FirstWebAppInDocker.Tests;

/// <summary>
/// Tester for hastegrad, visningshjelpere, rapporter/eksport,
/// audit-logg og at frivillige kan endre og slette egne ressurser.
/// </summary>
public class ReportAndUrgencyTests
{
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);


    // =========================================================
    // HASTEGRAD
    // =========================================================

    [Fact]
    public void Urgency_AcuteOverdueWithoutResources_IsCritical()
    {
        var need = CreateNeed(NeedPriority.Acute, createdAt: Now.AddHours(-30), deadline: Now.AddDays(-2));

        NeedUrgency urgency = NeedUrgencyService.Calculate(need, nearbyResources: 0, Now);

        // 50 (akutt) + 35 (passert frist) + 15 (venter > 24 t) + 20 (ingen ressurser) = 120 → maks 100
        Assert.Equal(100, urgency.Score);
        Assert.Equal(UrgencyLevel.Critical, urgency.Level);
        Assert.Contains(urgency.Reasons, reason => reason.Contains("Fristen er passert"));
        Assert.Equal("urgency-critical", urgency.CssClass);
    }

    [Fact]
    public void Urgency_PlannedNewWithManyResources_IsLow()
    {
        var need = CreateNeed(NeedPriority.Planned, createdAt: Now.AddMinutes(-10));

        NeedUrgency urgency = NeedUrgencyService.Calculate(need, nearbyResources: 5, Now);

        Assert.Equal(15, urgency.Score);
        Assert.Equal(UrgencyLevel.Low, urgency.Level);
        Assert.Equal("Lav", urgency.LevelName);
    }

    [Fact]
    public void Urgency_AssignedNeed_GetsReducedScore()
    {
        var open = CreateNeed(NeedPriority.Acute, createdAt: Now.AddMinutes(-10));
        var assigned = CreateNeed(NeedPriority.Acute, createdAt: Now.AddMinutes(-10), status: NeedStatus.Assigned);

        int openScore = NeedUrgencyService.Calculate(open, 5, Now).Score;
        int assignedScore = NeedUrgencyService.Calculate(assigned, 5, Now).Score;

        Assert.True(assignedScore < openScore);
    }

    [Fact]
    public void Urgency_ResolvedNeed_IsZero()
    {
        var need = CreateNeed(NeedPriority.Acute, createdAt: Now.AddDays(-3), status: NeedStatus.Resolved);

        NeedUrgency urgency = NeedUrgencyService.Calculate(need, 0, Now);

        Assert.Equal(0, urgency.Score);
        Assert.Equal(UrgencyLevel.Low, urgency.Level);
    }

    [Fact]
    public void Urgency_DeadlineToday_CountsAsWithinOneDay()
    {
        var need = CreateNeed(NeedPriority.Planned, createdAt: Now.AddMinutes(-10), deadline: Now.Date);

        NeedUrgency urgency = NeedUrgencyService.Calculate(need, 5, Now);

        Assert.Contains("Frist innen ett døgn", urgency.Reasons);
        Assert.Equal(45, urgency.Score);
        Assert.Equal(UrgencyLevel.High, urgency.Level);
    }

    [Fact]
    public async Task UrgencyService_CountsOnlyNearbyAvailableResourcesOfSameType()
    {
        using var database = CreateDatabase();

        var need = CreateNeed(NeedPriority.Acute, createdAt: DateTime.UtcNow);
        database.Needs.Add(need);

        database.Resources.AddRange(
            CreateResource("Nær og ledig", ResourceType.Generator, 58.15, 8.00),
            CreateResource("Feil type", ResourceType.Boat, 58.15, 8.00),
            CreateResource("Langt unna", ResourceType.Generator, 69.65, 18.95),
            CreateResource("Opptatt", ResourceType.Generator, 58.15, 8.00, ResourceStatus.Busy));

        await database.SaveChangesAsync();

        Dictionary<int, NeedUrgency> result =
            await new NeedUrgencyService(database).CalculateAsync(new[] { need });

        Assert.Equal(1, result[need.Id].NearbyResources);
    }


    // =========================================================
    // VISNINGSHJELPERE
    // =========================================================

    [Theory]
    [InlineData("Stålveien 9, 4629, KRISTIANSAND S – KRISTIANSAND kommune", "Stålveien 9, 4629, Kristiansand S – Kristiansand kommune")]
    [InlineData("Storgata 1, 0155 OSLO", "Storgata 1, 0155 Oslo")]
    [InlineData(null, null)]
    public void PrettyAddress_TitleCasesUpperCaseWords(string? input, string? expected)
    {
        Assert.Equal(expected, ResourceDisplay.PrettyAddress(input));
    }

    [Theory]
    [InlineData("Stålveien 9, 4629, KRISTIANSAND S – KRISTIANSAND kommune", "Kristiansand")]
    [InlineData("Gata 2, 5003 BERGEN – Bergen kommune", "Bergen")]
    [InlineData("Bare fritekst", "Ukjent kommune")]
    [InlineData(null, "Ukjent kommune")]
    public void Municipality_IsReadFromAddress(string? address, string expected)
    {
        Assert.Equal(expected, ResourceDisplay.Municipality(address));
    }

    [Fact]
    public void CountText_UsesSingularAndPlural()
    {
        Assert.Equal("1 ressurs", ResourceDisplay.CountText(1, "ressurs", "ressurser"));
        Assert.Equal("3 ressurser", ResourceDisplay.CountText(3, "ressurs", "ressurser"));
        Assert.Equal("0 ressurser", ResourceDisplay.CountText(0, "ressurs", "ressurser"));
    }

    [Fact]
    public void Icon_ReturnsSvgForEveryResourceType()
    {
        foreach (ResourceType type in Enum.GetValues<ResourceType>())
        {
            string svg = ResourceDisplay.Icon(type);

            Assert.StartsWith("<svg", svg);
            Assert.Contains("currentColor", svg);
        }
    }


    // =========================================================
    // EKSPORT
    // =========================================================

    [Theory]
    [InlineData("Vanlig tekst", "Vanlig tekst")]
    [InlineData("Med; semikolon", "\"Med; semikolon\"")]
    [InlineData("Med \"anførsel\"", "\"Med \"\"anførsel\"\"\"")]
    [InlineData("=SUM(A1:A2)", "'=SUM(A1:A2)")]
    [InlineData("Linje 1\nLinje 2", "Linje 1 Linje 2")]
    [InlineData(null, "")]
    public void Csv_EscapesFieldsAndBlocksFormulas(string? input, string expected)
    {
        Assert.Equal(expected, ReportService.Csv(input));
    }

    [Fact]
    public void ToCsv_HasBomHeaderAndOneLinePerResource()
    {
        var rows = new[]
        {
            ReportService.ToRow(CreateResource("Aggregat", ResourceType.Generator, 58.15, 8.00), Now),
            ReportService.ToRow(CreateResource("Båt", ResourceType.Boat, 58.15, 8.00), Now)
        };

        byte[] bytes = ReportService.ToCsv(rows);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());

        string[] lines =
            Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Id;Navn;Type", lines[0]);
        Assert.Contains("Aggregat;Strøm / aggregat", lines[1]);
        Assert.Contains("58.150000", lines[1]);
    }

    [Fact]
    public void ToJson_WritesReadableNorwegianText()
    {
        var rows = new[] { ReportService.ToRow(CreateResource("Båt", ResourceType.Boat, 58.15, 8.00), Now) };

        string json = Encoding.UTF8.GetString(ReportService.ToJson(rows, Now));

        Assert.Contains("\"count\": 1", json);
        Assert.Contains("\"name\": \"Båt\"", json);
        Assert.Contains("\"type\": \"Boat\"", json);
        Assert.Contains("\"municipality\": \"Kristiansand\"", json);
    }

    [Theory]
    [InlineData("Kristiansand S", "kristiansand-s")]
    [InlineData("Bærum", "baerum")]
    [InlineData("  ", "kommune")]
    public void Slug_IsSafeForFileNames(string input, string expected)
    {
        Assert.Equal(expected, ReportController.Slug(input));
    }

    [Fact]
    public async Task ResourceRows_FilterOnMunicipalityTypeAndAvailability()
    {
        using var database = CreateDatabase();

        database.Resources.AddRange(
            CreateResource("A", ResourceType.Generator, 58.15, 8.00),
            CreateResource("B", ResourceType.Boat, 58.15, 8.00),
            CreateResource("C", ResourceType.Generator, 58.15, 8.00, ResourceStatus.Unavailable),
            CreateResource("D", ResourceType.Generator, 60.39, 5.32, address: "Gata 2, 5003 BERGEN – BERGEN kommune"));

        await database.SaveChangesAsync();

        var service = new ReportService(database);

        List<ResourceReportRow> kristiansand =
            await service.GetResourceRowsAsync("Kristiansand", ResourceType.Generator, onlyAvailable: true);

        Assert.Equal("A", Assert.Single(kristiansand).Name);

        List<string> municipalities = await service.GetMunicipalitiesAsync();

        Assert.Equal(new[] { "Bergen", "Kristiansand" }, municipalities);
    }


    // =========================================================
    // SITUASJONSRAPPORT
    // =========================================================

    [Fact]
    public async Task SituationReport_FindsGapsAndSummarizesWithoutAi()
    {
        using var database = CreateDatabase();

        database.Needs.AddRange(
            CreateNeed(NeedPriority.Acute, createdAt: DateTime.UtcNow.AddHours(-2), type: ResourceType.Boat),
            CreateNeed(NeedPriority.Planned, createdAt: DateTime.UtcNow.AddHours(-2), type: ResourceType.Generator),
            CreateNeed(NeedPriority.Planned, createdAt: DateTime.UtcNow.AddDays(-5), status: NeedStatus.Resolved));

        database.Resources.Add(CreateResource("Aggregat", ResourceType.Generator, 58.15, 8.00));

        await database.SaveChangesAsync();

        SituationReportViewModel report = await new ReportService(database).BuildSituationAsync();

        Assert.Equal(2, report.OpenNeeds);
        Assert.Equal(1, report.AcuteOpenNeeds);
        Assert.Equal(1, report.ResolvedNeeds);
        Assert.Equal(1, report.AvailableResources);

        TypeCoverageRow boats = Assert.Single(report.Coverage, row => row.Type == ResourceType.Boat);
        Assert.True(boats.IsGap);

        Assert.Contains(report.Uncovered, row => row.TypeName == "Båt");
        Assert.Contains(report.AutomaticSummary, line => line.Contains("båt"));
        Assert.Equal(ResourceType.Boat.ToString(), report.Coverage[0].Type.ToString());

        string facts = ReportService.BuildAiFacts(report);
        Assert.Contains("MANGLER", facts);
        Assert.DoesNotContain("@", facts);
    }

    [Fact]
    public void JoinNorwegian_UsesOgBeforeLastItem()
    {
        Assert.Equal("", ReportService.JoinNorwegian(Array.Empty<string>()));
        Assert.Equal("båt", ReportService.JoinNorwegian(new[] { "båt" }));
        Assert.Equal("båt og drone", ReportService.JoinNorwegian(new[] { "båt", "drone" }));
        Assert.Equal("båt, drone og lokaler", ReportService.JoinNorwegian(new[] { "båt", "drone", "lokaler" }));
    }

    [Fact]
    public void ReportController_IsOnlyForPublicActorsAndAdmins()
    {
        var attribute =
            (AuthorizeAttribute)Attribute.GetCustomAttribute(typeof(ReportController), typeof(AuthorizeAttribute))!;

        Assert.Contains(IdentitySeeder.PublicActorRole, attribute.Roles);
        Assert.Contains(IdentitySeeder.AdminRole, attribute.Roles);
        Assert.DoesNotContain(IdentitySeeder.ResourceProviderRole, attribute.Roles);
    }


    // =========================================================
    // AUDIT-LOGG
    // =========================================================

    [Fact]
    public async Task AuditService_SavesEntryWithUserAndTruncatesDetails()
    {
        using var database = CreateDatabase();

        var audit = new AuditService(database, NullLogger<AuditService>.Instance);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "user-1"),
            new Claim(ClaimTypes.Email, "ola@example.com")
        }, "Test"));

        await audit.LogAsync(user, AuditActions.ResourceCreated, new string('x', 900), "127.0.0.1");

        AuditLogEntry entry = Assert.Single(await database.AuditLog.ToListAsync());

        Assert.Equal("user-1", entry.UserId);
        Assert.Equal("ola@example.com", entry.UserEmail);
        Assert.Equal(AuditActions.ResourceCreated, entry.Action);
        Assert.Equal(500, entry.Details.Length);
        Assert.Equal("Ressurs registrert", AuditActions.NameOf(entry.Action));
    }

    [Fact]
    public async Task AuditExtension_DoesNothingWithoutServices()
    {
        using var database = CreateDatabase();
        var controller = CreateResourceController(database, CreateUser());

        // Ingen RequestServices i testen: skal ikke kaste.
        await controller.AuditAsync(AuditActions.Login, "test");

        Assert.Empty(await database.AuditLog.ToListAsync());
    }


    // =========================================================
    // ENDRE OG SLETTE EGNE RESSURSER
    // =========================================================

    [Fact]
    public async Task DeleteResource_RemovesOwnResourceWithoutHistory()
    {
        using var database = CreateDatabase();
        var user = CreateUser();

        Resource resource = CreateResource("Aggregat", ResourceType.Generator, 58.15, 8.00, ownerUserId: user.Id);
        database.Resources.Add(resource);
        await database.SaveChangesAsync();

        var controller = CreateResourceController(database, user);

        Assert.IsType<RedirectToActionResult>(await controller.DeleteResource(resource.Id));
        Assert.Empty(await database.Resources.ToListAsync());
    }

    [Fact]
    public async Task DeleteResource_KeepsResourceWithHistory()
    {
        using var database = CreateDatabase();
        var user = CreateUser();

        Resource resource = CreateResource("Aggregat", ResourceType.Generator, 58.15, 8.00, ownerUserId: user.Id);
        var need = CreateNeed(NeedPriority.Acute, createdAt: DateTime.UtcNow);
        database.Resources.Add(resource);
        database.Needs.Add(need);
        await database.SaveChangesAsync();

        database.NeedAssignments.Add(new NeedAssignment
        {
            NeedId = need.Id,
            ResourceId = resource.Id,
            RequestedByUserId = "actor-1",
            Status = AssignmentStatus.Declined,
            CreatedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync();

        var controller = CreateResourceController(database, user);

        Assert.IsType<RedirectToActionResult>(await controller.DeleteResource(resource.Id));
        Assert.Single(await database.Resources.ToListAsync());
    }

    [Fact]
    public async Task DeleteResource_CannotDeleteSomeoneElsesResource()
    {
        using var database = CreateDatabase();

        Resource resource = CreateResource("Annens", ResourceType.Generator, 58.15, 8.00, ownerUserId: "someone-else");
        database.Resources.Add(resource);
        await database.SaveChangesAsync();

        var controller = CreateResourceController(database, CreateUser());

        Assert.IsType<NotFoundResult>(await controller.DeleteResource(resource.Id));
        Assert.Single(await database.Resources.ToListAsync());
    }

    [Fact]
    public async Task EditResource_Get_PrefillsFormFromStoredAddress()
    {
        using var database = CreateDatabase();
        var user = CreateUser();

        Resource resource = CreateResource("Aggregat", ResourceType.Generator, 58.15, 8.00, ownerUserId: user.Id);
        database.Resources.Add(resource);
        await database.SaveChangesAsync();

        var controller = CreateResourceController(database, user);

        var view = Assert.IsType<ViewResult>(await controller.EditResource(resource.Id));
        var model = Assert.IsType<RegisterResourceViewModel>(view.Model);

        Assert.Equal("RegisterResource", view.ViewName);
        Assert.Equal(resource.Id, view.ViewData["EditResourceId"]);
        Assert.Equal("Aggregat", model.ResourceName);
        Assert.Equal("Stålveien 9", model.StreetAddress);
        Assert.Equal("KRISTIANSAND", model.Municipality);
        Assert.Equal(3, model.AvailabilityMonths);
    }


    // =========================================================
    // HJELPERE
    // =========================================================

    private static ApplicationDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new ApplicationDbContext(options);
    }

    private static ApplicationUser CreateUser()
    {
        return new ApplicationUser
        {
            Id = "resource-provider-123",
            Name = "Ola Nordmann",
            UserName = "ola@example.com",
            Email = "ola@example.com",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static Need CreateNeed(
        NeedPriority priority,
        DateTime createdAt,
        DateTime? deadline = null,
        NeedStatus status = NeedStatus.New,
        ResourceType type = ResourceType.Generator)
    {
        return new Need
        {
            ResourceType = type,
            Priority = priority,
            Quantity = 1,
            Description = "Testbehov",
            Latitude = 58.1467,
            Longitude = 7.9956,
            OwnerEmail = "actor@example.com",
            CreatedAt = createdAt,
            Deadline = deadline,
            Status = status
        };
    }

    private static Resource CreateResource(
        string name,
        ResourceType type,
        double latitude,
        double longitude,
        ResourceStatus status = ResourceStatus.Available,
        string? ownerUserId = null,
        string address = "Stålveien 9, 4629, KRISTIANSAND S – KRISTIANSAND kommune")
    {
        return new Resource
        {
            ResourceName = name,
            ResourceType = type,
            Description = "Testressurs",
            Quantity = 1,
            Availability = "3 måneder; til 2099-01-01",
            Address = address,
            Latitude = latitude,
            Longitude = longitude,
            OwnerEmail = "ola@example.com",
            OwnerUserId = ownerUserId,
            ContactEmail = "kontakt@example.com",
            CreatedAt = DateTime.UtcNow,
            Status = status
        };
    }

    private static ResourceController CreateResourceController(ApplicationDbContext database, ApplicationUser user)
    {
        var userManager =
            new Mock<UserManager<ApplicationUser>>(
                new Mock<IUserStore<ApplicationUser>>().Object,
                null!, null!, null!, null!, null!, null!, null!, null!);

        userManager
            .Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, IdentitySeeder.ResourceProviderRole)
        }, "Test");

        return new ResourceController(database, userManager.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            },
            TempData = new Mock<ITempDataDictionary>().Object
        };
    }
}
