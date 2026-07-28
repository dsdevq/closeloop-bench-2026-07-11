namespace Api.Features.Deals;

public sealed record DealResponse(
    Guid Id,
    string Title,
    decimal Amount,
    DateOnly? CloseDate,
    Guid OwnerId,
    Guid PipelineId,
    Guid PipelineStageId,
    Guid? CompanyId,
    Guid? ContactId);

public sealed record CreateDealRequest(
    string Title,
    decimal Amount,
    Guid OwnerId,
    Guid PipelineId,
    Guid PipelineStageId,
    DateOnly? CloseDate = null,
    Guid? CompanyId = null,
    Guid? ContactId = null);

public sealed record PatchDealStageRequest(Guid StageId, Guid? OwnerId = null);
