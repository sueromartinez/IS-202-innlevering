using System.ComponentModel.DataAnnotations;
using FirstWebAppInDocker.Controllers;
using FirstWebAppInDocker.Data;
using FirstWebAppInDocker.Models;
using FirstWebAppInDocker.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FirstWebAppInDocker.Tests;

public class ResourceProviderTests
{
    // Verifies the values passed to persistence without modifying a real database.
    private sealed class RecordingContext : ApplicationDbContext
    {
        public bool Saved { get; private set; }
        public RecordingContext() : base(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql("Server=localhost;Database=test;User=test;Password=test",
                new MariaDbServerVersion(new Version(11, 4, 0))).Options) { }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saved = true;
            return Task.FromResult(1);
        }
    }

    private sealed class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> values = new();
        public bool IsAvailable => true;
        public string Id => "test";
        public IEnumerable<string> Keys => values.Keys;
        public void Clear() => values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => values.Remove(key);
        public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => values.TryGetValue(key, out value!);
    }

    private static ResourceController Controller(RecordingContext db, string? role = "ResourceProvider")
    {
        var context = new DefaultHttpContext { Session = new TestSession() };
        if (role != null)
        {
            context.Session.SetString("UserEmail", "owner@example.test");
            context.Session.SetString("UserRole", role);
        }
        return new ResourceController(db) { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    private static RegisterResourceViewModel Resource() => new()
    {
        ResourceName = " Generator ", ResourceType = ResourceType.Generator,
        Description = " Backup power ", Quantity = 2, Capacity = " 25 kVA ",
        Availability = "Tilgjengelig", Address = " Harbour ",
        Latitude = 58.15, Longitude = 8.0, ContactEmail = " contact@example.test ",
        ContactPhoneCountryCode = "+47", ContactPhoneNumber = "1234 5678"
    };

    [Fact]
    public async Task Registration_PassesFieldsAndSessionOwnerToPersistence()
    {
        using var db = new RecordingContext();
        var result = await Controller(db).RegisterResource(Resource());
        Assert.Equal("ResourceOverview", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.True(db.Saved);
        var saved = Assert.Single(db.ChangeTracker.Entries<Resource>()).Entity;
        Assert.Equal("owner@example.test", saved.OwnerEmail);
        Assert.Equal("Generator", saved.ResourceName);
        Assert.Equal("Backup power", saved.Description);
        Assert.Equal(2, saved.Quantity);
        Assert.Equal("25 kVA", saved.Capacity);
        Assert.Equal("Tilgjengelig", saved.Availability);
        Assert.Equal("Harbour", saved.Address);
        Assert.Equal(58.15, saved.Latitude);
        Assert.Equal(8.0, saved.Longitude);
        Assert.Equal("contact@example.test", saved.ContactEmail);
        Assert.Equal("+47", saved.ContactPhoneCountryCode);
        Assert.Equal("12345678", saved.ContactPhoneNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("PublicActor")]
    public async Task Registration_RejectsVisitorsAndOtherRoles(string? role)
    {
        using var db = new RecordingContext();
        Assert.IsType<RedirectToActionResult>(await Controller(db, role).RegisterResource(Resource()));
        Assert.False(db.Saved);
        Assert.Empty(db.ChangeTracker.Entries<Resource>());
    }

    [Fact]
    public async Task Registration_InvalidModelDoesNotSave()
    {
        using var db = new RecordingContext();
        var controller = Controller(db);
        controller.ModelState.AddModelError("ResourceName", "Required");
        Assert.IsType<ViewResult>(await controller.RegisterResource(Resource()));
        Assert.False(db.Saved);
    }

    [Fact]
    public void Validation_RequiresNameAndRejectsInvalidStatusAndCoordinates()
    {
        var model = new RegisterResourceViewModel
        {
            Description = "Test", ResourceType = ResourceType.Generator,
            Availability = "unknown", Latitude = 100, Longitude = 200
        };
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains("ResourceName"));
        var custom = model.Validate(new ValidationContext(model)).ToList();
        Assert.Contains(custom, result => result.MemberNames.Contains("Availability"));
        Assert.Contains(custom, result => result.MemberNames.Contains("Latitude"));
    }

    [Fact]
    public void Statistics_KeepUnspecifiedAvailabilitySeparate()
    {
        var model = new DashboardViewModel { Resources = new()
        {
            new() { Availability = "Tilgjengelig", Quantity = 5 },
            new() { Availability = "Ikke tilgjengelig" },
            new() { Availability = null },
            new() { Availability = "Call first" }
        }};
        Assert.Equal(4, model.ResourceCount);
        Assert.Equal(1, model.AvailableCount);
        Assert.Equal(1, model.UnavailableCount);
        Assert.Equal(2, model.UnspecifiedCount);
    }
}
