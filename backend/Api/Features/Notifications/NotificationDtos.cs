namespace Api.Features.Notifications;

public sealed record NotificationItemResponse(
    Guid Id,
    string Trigger,
    string Title,
    string? Body,
    Guid? RelatedEntityId,
    string? RelatedEntityType,
    bool IsRead,
    DateTime CreatedAt);

public sealed record NotificationsListResponse(
    NotificationItemResponse[] Items,
    int UnreadCount);
