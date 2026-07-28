using Domain.Entities;
using Domain.Interfaces;

namespace Infrastructure.Services;

public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly CrmDbContext _db;

    public NotificationDispatcher(CrmDbContext db) => _db = db;

    public async Task DealAssignedAsync(Deal deal, Guid previousOwnerId, CancellationToken ct = default)
    {
        _db.Notifications.Add(Notification.Create(
            recipientUserId: deal.OwnerId,
            trigger: NotificationTrigger.DealAssigned,
            title: "Deal assigned to you",
            body: deal.Title,
            relatedEntityId: deal.Id,
            relatedEntityType: NotificationEntityType.Deal));
        await _db.SaveChangesAsync(ct);
    }

    public async Task DealStageChangedAsync(Deal deal, PipelineStage toStage, CancellationToken ct = default)
    {
        _db.Notifications.Add(Notification.Create(
            recipientUserId: deal.OwnerId,
            trigger: NotificationTrigger.DealStageChanged,
            title: $"Deal moved to {toStage.Name}",
            body: deal.Title,
            relatedEntityId: deal.Id,
            relatedEntityType: NotificationEntityType.Deal));
        await _db.SaveChangesAsync(ct);
    }

    public async Task ContactAssignedAsync(Contact contact, Guid previousOwnerId, CancellationToken ct = default)
    {
        _db.Notifications.Add(Notification.Create(
            recipientUserId: contact.OwnerId,
            trigger: NotificationTrigger.ContactAssigned,
            title: "Contact assigned to you",
            body: contact.Name,
            relatedEntityId: contact.Id,
            relatedEntityType: NotificationEntityType.Contact));
        await _db.SaveChangesAsync(ct);
    }

    public async Task ActivityMentionAsync(
        Activity activity,
        IReadOnlyList<Guid> mentionedUserIds,
        CancellationToken ct = default)
    {
        foreach (var userId in mentionedUserIds)
        {
            _db.Notifications.Add(Notification.Create(
                recipientUserId: userId,
                trigger: NotificationTrigger.ActivityMention,
                title: "You were mentioned in a note",
                relatedEntityId: activity.Id,
                relatedEntityType: NotificationEntityType.Activity));
        }
        await _db.SaveChangesAsync(ct);
    }
}
