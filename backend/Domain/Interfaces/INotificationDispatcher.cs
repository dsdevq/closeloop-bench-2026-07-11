using Domain.Entities;

namespace Domain.Interfaces;

public interface INotificationDispatcher
{
    Task DealAssignedAsync(Deal deal, Guid previousOwnerId, CancellationToken ct = default);
    Task DealStageChangedAsync(Deal deal, PipelineStage toStage, CancellationToken ct = default);
    Task ContactAssignedAsync(Contact contact, Guid previousOwnerId, CancellationToken ct = default);
    Task ActivityMentionAsync(Activity activity, IReadOnlyList<Guid> mentionedUserIds, CancellationToken ct = default);
}
