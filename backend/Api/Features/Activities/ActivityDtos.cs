using Domain.Entities;

namespace Api.Features.Activities;

public sealed record CreateActivityRequest(
    ActivityType Type,
    string Note,
    DateTime OccurredAt,
    Guid? ContactId,
    Guid? CompanyId,
    Guid? DealId);

public sealed record ActivityResponse(
    Guid Id,
    string Type,
    string Note,
    DateTime OccurredAt,
    Guid? ContactId,
    Guid? CompanyId,
    Guid? DealId);
