using Domain.Entities;
using Domain.Interfaces;

namespace Infrastructure.Services;

public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly CrmDbContext _db;

    public NotificationDispatcher(CrmDbContext db) => _db = db;

    // Requires Deal.OwnerId — deferred until the ownership slice adds that field.
    public Task DealAssignedAsync(Deal deal, Guid previousOwnerId, CancellationToken ct = default)
        => Task.CompletedTask;

    // Requires Deal.OwnerId — deferred until the ownership slice adds that field.
    public Task DealStageChangedAsync(Deal deal, PipelineStage toStage, CancellationToken ct = default)
        => Task.CompletedTask;

    // Requires Contact.OwnerId — deferred until the ownership slice adds that field.
    public Task ContactAssignedAsync(Contact contact, Guid previousOwnerId, CancellationToken ct = default)
        => Task.CompletedTask;

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
