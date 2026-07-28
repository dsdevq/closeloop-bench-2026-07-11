using Domain.Common;

namespace Domain.Entities;

public sealed class Deal : Entity
{
    public string Title { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public DateOnly? CloseDate { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid PipelineId { get; private set; }
    public Guid PipelineStageId { get; private set; }
    public Guid? CompanyId { get; private set; }
    public Guid? ContactId { get; private set; }

    private Deal() { }

    public static Deal Create(
        string title,
        decimal amount,
        Guid ownerId,
        Guid pipelineId,
        Guid pipelineStageId,
        DateOnly? closeDate = null,
        Guid? companyId = null,
        Guid? contactId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Deal title is required.", nameof(title));
        if (amount < 0)
            throw new ArgumentException("Amount must be non-negative.", nameof(amount));
        if (ownerId == Guid.Empty)
            throw new ArgumentException("OwnerId must not be empty.", nameof(ownerId));
        if (pipelineId == Guid.Empty)
            throw new ArgumentException("PipelineId must not be empty.", nameof(pipelineId));
        if (pipelineStageId == Guid.Empty)
            throw new ArgumentException("PipelineStageId must not be empty.", nameof(pipelineStageId));

        return new Deal
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            Amount = amount,
            CloseDate = closeDate,
            OwnerId = ownerId,
            PipelineId = pipelineId,
            PipelineStageId = pipelineStageId,
            CompanyId = companyId,
            ContactId = contactId,
        };
    }

    public void AdvanceTo(PipelineStage stage)
    {
        if (stage.PipelineId != PipelineId)
            throw new ArgumentException(
                "Stage does not belong to this deal's pipeline.", nameof(stage));

        PipelineStageId = stage.Id;
    }
}
