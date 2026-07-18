using Domain.Common;

namespace Domain.Entities;

public sealed class Notification : Entity
{
    public Guid RecipientUserId { get; private init; }
    public NotificationTrigger Trigger { get; private init; }
    public string Title { get; private init; } = default!;
    public string? Body { get; private init; }
    public Guid? RelatedEntityId { get; private init; }
    public NotificationEntityType? RelatedEntityType { get; private init; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private init; }

    private Notification() { }

    public static Notification Create(
        Guid recipientUserId,
        NotificationTrigger trigger,
        string title,
        string? body = null,
        Guid? relatedEntityId = null,
        NotificationEntityType? relatedEntityType = null)
    {
        if (recipientUserId == Guid.Empty)
            throw new ArgumentException("RecipientUserId is required.", nameof(recipientUserId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        if (relatedEntityId.HasValue != relatedEntityType.HasValue)
            throw new ArgumentException("RelatedEntityId and RelatedEntityType must both be set or both null.");

        return new Notification
        {
            Id = Guid.NewGuid(),
            RecipientUserId = recipientUserId,
            Trigger = trigger,
            Title = title,
            Body = body,
            RelatedEntityId = relatedEntityId,
            RelatedEntityType = relatedEntityType,
            IsRead = false,
            CreatedAt = DateTime.UtcNow,
        };
    }

    public void MarkRead() => IsRead = true;
}
