using Domain.Common;

namespace Domain.Entities;

public sealed class Deal : Entity
{
    public decimal Amount { get; private set; }
    public Guid PipelineId { get; private set; }
    public Guid PipelineStageId { get; private set; }
    public Guid? CompanyId { get; private set; }
    public Guid? ContactId { get; private set; }

    private Deal() { }

    public static Deal Create(
        decimal amount,
        Guid pipelineId,
        Guid pipelineStageId,
        Guid? companyId = null,
        Guid? contactId = null)
    {
        if (amount < 0)
            throw new ArgumentException("Amount must be non-negative.", nameof(amount));
        if (pipelineId == Guid.Empty)
            throw new ArgumentException("PipelineId must not be empty.", nameof(pipelineId));
        if (pipelineStageId == Guid.Empty)
            throw new ArgumentException("PipelineStageId must not be empty.", nameof(pipelineStageId));

        return new Deal
        {
            Id = Guid.NewGuid(),
            Amount = amount,
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
