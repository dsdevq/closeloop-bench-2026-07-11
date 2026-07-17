using System.Net;
using System.Net.Http.Json;
using Api.Features.Notifications;
using Domain.Entities;
using Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Api.Tests.Features.Notifications;

public sealed class NotificationsEndpointsTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public NotificationsEndpointsTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;");

                builder.ConfigureTestServices(services =>
                {
                    var dbName = "NotificationsTest_" + Guid.NewGuid();

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

    private async Task SeedNotificationAsync(Notification notification)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetNotifications_EmptyDb_ReturnsEmptyItems()
    {
        var userId = Guid.NewGuid();

        var response = await _client.GetAsync($"/notifications?userId={userId}&limit=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<NotificationsListResponse>();
        Assert.NotNull(body);
        Assert.Empty(body.Items);
        Assert.Equal(0, body.UnreadCount);
    }

    [Fact]
    public async Task GetNotifications_WithNotifications_ReturnsItemsForUser()
    {
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        await SeedNotificationAsync(Notification.Create(userId, NotificationTrigger.ActivityMention, "Test title"));
        await SeedNotificationAsync(Notification.Create(otherId, NotificationTrigger.DealRotting, "Other user"));

        var response = await _client.GetAsync($"/notifications?userId={userId}&limit=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<NotificationsListResponse>();
        Assert.NotNull(body);
        Assert.Single(body.Items);
        Assert.Equal("Test title", body.Items[0].Title);
        Assert.Equal("ActivityMention", body.Items[0].Trigger);
        Assert.False(body.Items[0].IsRead);
        Assert.Equal(1, body.UnreadCount);
    }

    [Fact]
    public async Task GetNotifications_FilterByIsReadFalse_ReturnsOnlyUnread()
    {
        var userId = Guid.NewGuid();

        var unread = Notification.Create(userId, NotificationTrigger.ActivityMention, "Unread");
        var read = Notification.Create(userId, NotificationTrigger.DealRotting, "Read one");
        read.MarkRead();

        await SeedNotificationAsync(unread);
        await SeedNotificationAsync(read);

        var response = await _client.GetAsync($"/notifications?userId={userId}&isRead=false&limit=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<NotificationsListResponse>();
        Assert.NotNull(body);
        Assert.Single(body.Items);
        Assert.Equal("Unread", body.Items[0].Title);
    }

    [Fact]
    public async Task PatchNotificationRead_ValidId_Returns204AndMarksRead()
    {
        var userId = Guid.NewGuid();
        var notification = Notification.Create(userId, NotificationTrigger.ActivityMention, "Mark me read");
        await SeedNotificationAsync(notification);

        var response = await _client.PatchAsync($"/notifications/{notification.Id}/read", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's now read via the list endpoint
        var listResponse = await _client.GetAsync($"/notifications?userId={userId}&isRead=false&limit=50");
        var body = await listResponse.Content.ReadFromJsonAsync<NotificationsListResponse>();
        Assert.NotNull(body);
        Assert.Empty(body.Items);
        Assert.Equal(0, body.UnreadCount);
    }

    [Fact]
    public async Task PatchNotificationRead_UnknownId_Returns404()
    {
        var response = await _client.PatchAsync($"/notifications/{Guid.NewGuid()}/read", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostReadAll_MarksAllUnreadForUserAsRead()
    {
        var userId = Guid.NewGuid();

        await SeedNotificationAsync(Notification.Create(userId, NotificationTrigger.ActivityMention, "First"));
        await SeedNotificationAsync(Notification.Create(userId, NotificationTrigger.DealRotting, "Second"));

        var response = await _client.PostAsync($"/notifications/read-all?userId={userId}", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var listResponse = await _client.GetAsync($"/notifications?userId={userId}&limit=50");
        var body = await listResponse.Content.ReadFromJsonAsync<NotificationsListResponse>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Items.Length);
        Assert.Equal(0, body.UnreadCount);
        Assert.All(body.Items, item => Assert.True(item.IsRead));
    }

    [Fact]
    public async Task GetNotifications_MissingUserId_Returns422()
    {
        var response = await _client.GetAsync("/notifications?limit=50");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
