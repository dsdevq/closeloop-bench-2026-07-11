using System.Net;
using System.Net.Http.Json;
using Api.Features.Activities;
using Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests.Features.Activities;

public sealed class ActivitiesEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ActivitiesEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;");

                builder.ConfigureTestServices(services =>
                {
                    var dbName = "ActivitiesTest_" + Guid.NewGuid();

                    var optConfigType = typeof(IDbContextOptionsConfiguration<CrmDbContext>);
                    foreach (var d in services.Where(d => d.ServiceType == optConfigType).ToList())
                        services.Remove(d);
                    foreach (var d in services.Where(d => d.ServiceType == typeof(DbContextOptions<CrmDbContext>)).ToList())
                        services.Remove(d);

                    services.AddDbContext<CrmDbContext>(options =>
                        options.UseInMemoryDatabase(dbName));
                });
            });
        _client = _factory.CreateClient();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task PostActivity_ValidRequest_Returns201WithCreatedActivity()
    {
        var contactId = Guid.NewGuid();
        var req = new CreateActivityRequest(
            ActivityType.Note,
            "Spoke with the client",
            new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            ContactId: contactId,
            CompanyId: null,
            DealId: null);

        var response = await _client.PostAsJsonAsync("/activities", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ActivityResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.Equal("Note", body.Type);
        Assert.Equal("Spoke with the client", body.Note);
        Assert.Equal(contactId, body.ContactId);
        Assert.Null(body.CompanyId);
        Assert.Null(body.DealId);
    }

    [Fact]
    public async Task PostActivity_WithMention_CreatesNotificationForMentionedUser()
    {
        var contactId = Guid.NewGuid();
        var mentionedUserId = Guid.NewGuid();
        var note = $"Hey @{mentionedUserId} please review this";

        var req = new CreateActivityRequest(
            ActivityType.Note,
            note,
            DateTime.UtcNow,
            ContactId: contactId,
            CompanyId: null,
            DealId: null);

        var response = await _client.PostAsJsonAsync("/activities", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var notifications = await db.Notifications
            .Where(n => n.RecipientUserId == mentionedUserId)
            .ToListAsync();

        Assert.Single(notifications);
        Assert.Equal(NotificationTrigger.ActivityMention, notifications[0].Trigger);
        Assert.False(notifications[0].IsRead);
    }

    [Fact]
    public async Task PostActivity_WithMultipleMentions_CreatesOneNotificationPerMentionedUser()
    {
        var contactId = Guid.NewGuid();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var note = $"@{user1} and @{user2} please check this";

        var req = new CreateActivityRequest(
            ActivityType.Note,
            note,
            DateTime.UtcNow,
            ContactId: contactId,
            CompanyId: null,
            DealId: null);

        var response = await _client.PostAsJsonAsync("/activities", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        Assert.Single(await db.Notifications.Where(n => n.RecipientUserId == user1).ToListAsync());
        Assert.Single(await db.Notifications.Where(n => n.RecipientUserId == user2).ToListAsync());
    }

    [Fact]
    public async Task PostActivity_DuplicateMentionOfSameUser_CreatesOnlyOneNotification()
    {
        var contactId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var note = $"@{userId} and @{userId} — duplicate mention";

        var req = new CreateActivityRequest(
            ActivityType.Note,
            note,
            DateTime.UtcNow,
            ContactId: contactId,
            CompanyId: null,
            DealId: null);

        var response = await _client.PostAsJsonAsync("/activities", req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var notifications = await db.Notifications
            .Where(n => n.RecipientUserId == userId)
            .ToListAsync();

        Assert.Single(notifications);
    }

    [Fact]
    public async Task PostActivity_NoAnchor_Returns422()
    {
        var req = new CreateActivityRequest(
            ActivityType.Note,
            "No anchor",
            DateTime.UtcNow,
            ContactId: null,
            CompanyId: null,
            DealId: null);

        var response = await _client.PostAsJsonAsync("/activities", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostActivity_MultipleAnchors_Returns422()
    {
        var req = new CreateActivityRequest(
            ActivityType.Note,
            "Over-anchored",
            DateTime.UtcNow,
            ContactId: Guid.NewGuid(),
            CompanyId: Guid.NewGuid(),
            DealId: null);

        var response = await _client.PostAsJsonAsync("/activities", req);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
