using Domain.Entities;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Infrastructure.Tests;

public sealed class NotificationDispatcherTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private CrmDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CrmDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options);

    // ── DealAssignedAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task DealAssignedAsync_CreatesNotificationForNewOwner()
    {
        var ownerId = Guid.NewGuid();
        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Prospect", 0);
        var deal = Deal.Create("Acme Renewal", 10_000m, ownerId, pipeline.Id, pipeline.Stages[0].Id);

        await using var ctx = CreateContext();
        ctx.Pipelines.Add(pipeline);
        ctx.Deals.Add(deal);
        await ctx.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(ctx);
        await dispatcher.DealAssignedAsync(deal);

        await using var read = CreateContext();
        var notification = await read.Notifications.SingleAsync();

        Assert.Equal(ownerId, notification.RecipientUserId);
        Assert.Equal(NotificationTrigger.DealAssigned, notification.Trigger);
        Assert.Equal("Deal assigned to you", notification.Title);
        Assert.Equal("Acme Renewal", notification.Body);
        Assert.Equal(deal.Id, notification.RelatedEntityId);
        Assert.Equal(NotificationEntityType.Deal, notification.RelatedEntityType);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public async Task DealAssignedAsync_PersistsExactlyOneNotification()
    {
        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Lead", 0);
        var deal = Deal.Create("Test Deal", 500m, Guid.NewGuid(), pipeline.Id, pipeline.Stages[0].Id);

        await using var ctx = CreateContext();
        ctx.Pipelines.Add(pipeline);
        ctx.Deals.Add(deal);
        await ctx.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(ctx);
        await dispatcher.DealAssignedAsync(deal);

        await using var read = CreateContext();
        Assert.Equal(1, await read.Notifications.CountAsync());
    }

    // ── DealStageChangedAsync ────────────────────────────────────────────────

    [Fact]
    public async Task DealStageChangedAsync_CreatesNotificationForDealOwner()
    {
        var ownerId = Guid.NewGuid();
        var pipeline = Pipeline.Create("Growth");
        pipeline.AddStage("Lead", 0);
        pipeline.AddStage("Negotiation", 1, 70);
        var deal = Deal.Create("Beta Corp", 25_000m, ownerId, pipeline.Id, pipeline.Stages[0].Id);
        var toStage = pipeline.Stages[1];

        await using var ctx = CreateContext();
        ctx.Pipelines.Add(pipeline);
        ctx.Deals.Add(deal);
        await ctx.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(ctx);
        await dispatcher.DealStageChangedAsync(deal, toStage);

        await using var read = CreateContext();
        var notification = await read.Notifications.SingleAsync();

        Assert.Equal(ownerId, notification.RecipientUserId);
        Assert.Equal(NotificationTrigger.DealStageChanged, notification.Trigger);
        Assert.Equal("Deal moved to Negotiation", notification.Title);
        Assert.Equal("Beta Corp", notification.Body);
        Assert.Equal(deal.Id, notification.RelatedEntityId);
        Assert.Equal(NotificationEntityType.Deal, notification.RelatedEntityType);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public async Task DealStageChangedAsync_TitleIncludesStageName()
    {
        var pipeline = Pipeline.Create("Enterprise");
        pipeline.AddStage("Discovery", 0);
        pipeline.AddStage("Closed Won", 1, 100);
        var deal = Deal.Create("Gamma Deal", 50_000m, Guid.NewGuid(), pipeline.Id, pipeline.Stages[0].Id);

        await using var ctx = CreateContext();
        ctx.Pipelines.Add(pipeline);
        ctx.Deals.Add(deal);
        await ctx.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(ctx);
        await dispatcher.DealStageChangedAsync(deal, pipeline.Stages[1]);

        await using var read = CreateContext();
        var notification = await read.Notifications.SingleAsync();
        Assert.Equal("Deal moved to Closed Won", notification.Title);
    }

    // ── ContactAssignedAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ContactAssignedAsync_CreatesNotificationForNewOwner()
    {
        var ownerId = Guid.NewGuid();
        var contact = Contact.Create("Jane Doe", "jane@example.com", null, null, ownerId);

        await using var ctx = CreateContext();
        ctx.Contacts.Add(contact);
        await ctx.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(ctx);
        await dispatcher.ContactAssignedAsync(contact);

        await using var read = CreateContext();
        var notification = await read.Notifications.SingleAsync();

        Assert.Equal(ownerId, notification.RecipientUserId);
        Assert.Equal(NotificationTrigger.ContactAssigned, notification.Trigger);
        Assert.Equal("Contact assigned to you", notification.Title);
        Assert.Equal("Jane Doe", notification.Body);
        Assert.Equal(contact.Id, notification.RelatedEntityId);
        Assert.Equal(NotificationEntityType.Contact, notification.RelatedEntityType);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public async Task ContactAssignedAsync_PersistsExactlyOneNotification()
    {
        var contact = Contact.Create("Bob Smith", "bob@example.com", null, null, Guid.NewGuid());

        await using var ctx = CreateContext();
        ctx.Contacts.Add(contact);
        await ctx.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(ctx);
        await dispatcher.ContactAssignedAsync(contact);

        await using var read = CreateContext();
        Assert.Equal(1, await read.Notifications.CountAsync());
    }

    // ── ActivityMentionAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ActivityMentionAsync_CreatesOneNotificationPerMentionedUser()
    {
        var contact = Contact.Create("Alice", "alice@example.com", null, null, Guid.NewGuid());
        var activity = Activity.Create(ActivityType.Note, "See @user1 and @user2", DateTime.UtcNow, contactId: contact.Id);
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        await using var ctx = CreateContext();
        ctx.Contacts.Add(contact);
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(ctx);
        await dispatcher.ActivityMentionAsync(activity, [userId1, userId2]);

        await using var read = CreateContext();
        var notifications = await read.Notifications.ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, n => n.RecipientUserId == userId1);
        Assert.Contains(notifications, n => n.RecipientUserId == userId2);
        Assert.All(notifications, n =>
        {
            Assert.Equal(NotificationTrigger.ActivityMention, n.Trigger);
            Assert.Equal("You were mentioned in a note", n.Title);
            Assert.Equal(activity.Id, n.RelatedEntityId);
            Assert.Equal(NotificationEntityType.Activity, n.RelatedEntityType);
            Assert.False(n.IsRead);
        });
    }

    [Fact]
    public async Task ActivityMentionAsync_DeduplicatesMentionedUserIds()
    {
        var contact = Contact.Create("Bob", "bob@example.com", null, null, Guid.NewGuid());
        var activity = Activity.Create(ActivityType.Note, "Hello!", DateTime.UtcNow, contactId: contact.Id);
        var userId = Guid.NewGuid();

        await using var ctx = CreateContext();
        ctx.Contacts.Add(contact);
        ctx.Activities.Add(activity);
        await ctx.SaveChangesAsync();

        var dispatcher = new NotificationDispatcher(ctx);
        await dispatcher.ActivityMentionAsync(activity, [userId, userId]);

        await using var read = CreateContext();
        Assert.Equal(1, await read.Notifications.CountAsync());
    }
}
