using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Pipelines;

public static class PipelinesEndpoints
{
    public static IEndpointRouteBuilder MapPipelinesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/pipelines");

        group.MapGet("/", ListPipelines);
        group.MapGet("/{id:guid}", GetPipeline);
        group.MapPost("/", CreatePipeline);

        return app;
    }

    private static async Task<IResult> ListPipelines(CrmDbContext db)
    {
        var pipelines = await db.Pipelines
            .Include(p => p.Stages)
            .Select(p => new PipelineResponse(
                p.Id,
                p.Name,
                p.RottingThresholdDays,
                p.Stages.OrderBy(s => s.Order)
                    .Select(s => new PipelineStageResponse(s.Id, s.Name, s.Order, s.WinProbability))
                    .ToList()))
            .ToListAsync();
        return Results.Ok(pipelines);
    }

    private static async Task<IResult> GetPipeline(Guid id, CrmDbContext db)
    {
        var pipeline = await db.Pipelines
            .Include(p => p.Stages)
            .Where(p => p.Id == id)
            .Select(p => new PipelineResponse(
                p.Id,
                p.Name,
                p.RottingThresholdDays,
                p.Stages.OrderBy(s => s.Order)
                    .Select(s => new PipelineStageResponse(s.Id, s.Name, s.Order, s.WinProbability))
                    .ToList()))
            .SingleOrDefaultAsync();

        return pipeline is null ? Results.NotFound() : Results.Ok(pipeline);
    }

    private static async Task<IResult> CreatePipeline(CreatePipelineRequest req, CrmDbContext db)
    {
        Pipeline pipeline;
        try
        {
            pipeline = Pipeline.Create(req.Name);

            if (req.Stages is not null)
            {
                foreach (var stageReq in req.Stages)
                    pipeline.AddStage(stageReq.Name, stageReq.Order, stageReq.WinProbability);
            }
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        db.Pipelines.Add(pipeline);
        await db.SaveChangesAsync();

        var response = new PipelineResponse(
            pipeline.Id,
            pipeline.Name,
            pipeline.RottingThresholdDays,
            pipeline.Stages
                .Select(s => new PipelineStageResponse(s.Id, s.Name, s.Order, s.WinProbability))
                .ToList());
        return Results.Created($"/pipelines/{pipeline.Id}", response);
    }
}
