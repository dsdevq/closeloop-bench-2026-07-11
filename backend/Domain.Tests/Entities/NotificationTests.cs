using Domain.Entities;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class NotificationTests
{
    [Fact]
    public void Create_WithValidMinimalArgs_ReturnsPopulatedNotification()
    {
        var recipientId = Guid.NewGuid();

        var notification = Notification.Create(
            recipientId,
            NotificationTrigger.ActivityMention,
            "You were mentioned in a note");

        Assert.NotEqual(Guid.Empty, notification.Id);
        Assert.Equal(recipientId, notification.RecipientUserId);
        Assert.Equal(NotificationTrigger.ActivityMention, notification.Trigger);
        Assert.Equal("You were mentioned in a note", notification.Title);
        Assert.Null(notification.Body);
        Assert.Null(notification.RelatedEntityId);
        Assert.Null(notification.RelatedEntityType);
        Assert.False(notification.IsRead);
    }

    [Fact]
    public void Create_WithAllOptionalArgs_PopulatesAllFields()
    {
        var recipientId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var notification = Notification.Create(
            recipientId,
            NotificationTrigger.DealStageChanged,
            "Deal moved to Negotiation",
            body: "Acme Q3 Renewal",
            relatedEntityId: entityId,
            relatedEntityType: NotificationEntityType.Deal);

        Assert.Equal(recipientId, notification.RecipientUserId);
        Assert.Equal(NotificationTrigger.DealStageChanged, notification.Trigger);
        Assert.Equal("Deal moved to Negotiation", notification.Title);
        Assert.Equal("Acme Q3 Renewal", notification.Body);
        Assert.Equal(entityId, notification.RelatedEntityId);
        Assert.Equal(NotificationEntityType.Deal, notification.RelatedEntityType);
    }

    [Fact]
    public void Create_SetsCreatedAtToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var notification = Notification.Create(Guid.NewGuid(), NotificationTrigger.TaskDue, "Task is due");

        Assert.True(notification.CreatedAt >= before);
        Assert.True(notification.CreatedAt <= DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(DateTimeKind.Utc, notification.CreatedAt.Kind);
    }

    [Fact]
    public void Create_AssignsDistinctIdToEachInstance()
    {
        var recipientId = Guid.NewGuid();

        var a = Notification.Create(recipientId, NotificationTrigger.ActivityMention, "First");
        var b = Notification.Create(recipientId, NotificationTrigger.ActivityMention, "Second");

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Create_WithEmptyRecipientUserId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Notification.Create(Guid.Empty, NotificationTrigger.ActivityMention, "Title"));

        Assert.Equal("recipientUserId", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankTitle_ThrowsArgumentException(string? title)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Notification.Create(Guid.NewGuid(), NotificationTrigger.ActivityMention, title!));

        Assert.Equal("title", ex.ParamName);
    }

    [Fact]
    public void Create_WithRelatedEntityIdButNoType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Notification.Create(
                Guid.NewGuid(),
                NotificationTrigger.DealAssigned,
                "Deal assigned",
                relatedEntityId: Guid.NewGuid(),
                relatedEntityType: null));
    }

    [Fact]
    public void Create_WithRelatedEntityTypeButNoId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Notification.Create(
                Guid.NewGuid(),
                NotificationTrigger.DealAssigned,
                "Deal assigned",
                relatedEntityId: null,
                relatedEntityType: NotificationEntityType.Deal));
    }

    [Fact]
    public void MarkRead_SetsIsReadToTrue()
    {
        var notification = Notification.Create(Guid.NewGuid(), NotificationTrigger.ActivityMention, "Test");
        Assert.False(notification.IsRead);

        notification.MarkRead();

        Assert.True(notification.IsRead);
    }

    [Fact]
    public void MarkRead_CalledTwice_RemainsRead()
    {
        var notification = Notification.Create(Guid.NewGuid(), NotificationTrigger.ActivityMention, "Test");

        notification.MarkRead();
        notification.MarkRead();

        Assert.True(notification.IsRead);
    }
}
