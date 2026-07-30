using Domain.Entities;

namespace Domain.Interfaces;

public interface INotificationDispatcher
{
    Task DealAssignedAsync(Deal deal, CancellationToken ct = default);
    Task DealStageChangedAsync(Deal deal, PipelineStage toStage, CancellationToken ct = default);
    Task ContactAssignedAsync(Contact contact, CancellationToken ct = default);
    Task ActivityMentionAsync(Activity activity, IReadOnlyList<Guid> mentionedUserIds, CancellationToken ct = default);
}
