namespace Api.Features.Pipelines;

public sealed record PipelineStageResponse(
    Guid Id,
    string Name,
    int Order,
    int? WinProbability);

public sealed record PipelineResponse(
    Guid Id,
    string Name,
    int? RottingThresholdDays,
    IReadOnlyList<PipelineStageResponse> Stages);

public sealed record CreatePipelineRequest(
    string Name,
    IReadOnlyList<CreatePipelineStageRequest>? Stages = null);

public sealed record CreatePipelineStageRequest(
    string Name,
    int Order,
    int? WinProbability = null);
