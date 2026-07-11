using Domain.Common;

namespace Domain.Entities;

public sealed class Activity : Entity
{
    public ActivityType Type { get; private set; }
    public string Note { get; private set; } = default!;
    public DateTime OccurredAt { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? CompanyId { get; private set; }
    public Guid? DealId { get; private set; }

    private Activity() { }

    public static Activity Create(
        ActivityType type,
        string note,
        DateTime occurredAt,
        Guid? contactId = null,
        Guid? companyId = null,
        Guid? dealId = null)
    {
        var anchorCount = (contactId.HasValue ? 1 : 0)
                        + (companyId.HasValue ? 1 : 0)
                        + (dealId.HasValue ? 1 : 0);

        if (anchorCount == 0)
            throw new ArgumentException(
                "Activity must be anchored to exactly one of ContactId, CompanyId, or DealId.");
        if (anchorCount > 1)
            throw new ArgumentException(
                "Activity must be anchored to exactly one of ContactId, CompanyId, or DealId.");

        return new Activity
        {
            Id = Guid.NewGuid(),
            Type = type,
            Note = note ?? string.Empty,
            OccurredAt = occurredAt,
            ContactId = contactId,
            CompanyId = companyId,
            DealId = dealId,
        };
    }
}
