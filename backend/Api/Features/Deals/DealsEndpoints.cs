using Domain.Entities;
using Domain.Interfaces;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Deals;

public static class DealsEndpoints
{
    public static IEndpointRouteBuilder MapDealsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/deals");

        group.MapGet("/", ListDeals);
        group.MapGet("/{id:guid}", GetDeal);
        group.MapPost("/", CreateDeal);
        group.MapPatch("/{id:guid}/stage", PatchDealStage);

        return app;
    }

    private static async Task<IResult> ListDeals(CrmDbContext db)
    {
        var deals = await db.Deals
            .OrderBy(d => d.Title)
            .Take(200)
            .Select(d => new DealResponse(
                d.Id, d.Title, d.Amount, d.CloseDate, d.OwnerId,
                d.PipelineId, d.PipelineStageId, d.CompanyId, d.ContactId))
            .ToListAsync();
        return Results.Ok(deals);
    }

    private static async Task<IResult> GetDeal(Guid id, CrmDbContext db)
    {
        var deal = await db.Deals
            .Where(d => d.Id == id)
            .Select(d => new DealResponse(
                d.Id, d.Title, d.Amount, d.CloseDate, d.OwnerId,
                d.PipelineId, d.PipelineStageId, d.CompanyId, d.ContactId))
            .SingleOrDefaultAsync();

        return deal is null ? Results.NotFound() : Results.Ok(deal);
    }

    private static async Task<IResult> CreateDeal(CreateDealRequest req, CrmDbContext db)
    {
        var stage = await db.PipelineStages
            .Where(s => s.Id == req.PipelineStageId)
            .SingleOrDefaultAsync();

        if (stage is null || stage.PipelineId != req.PipelineId)
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["pipelineStageId"] = ["Stage does not belong to the given pipeline."] },
                statusCode: StatusCodes.Status422UnprocessableEntity);

        Deal deal;
        try
        {
            deal = Deal.Create(
                req.Title,
                req.Amount,
                req.OwnerId,
                req.PipelineId,
                req.PipelineStageId,
                req.CloseDate,
                req.CompanyId,
                req.ContactId);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        db.Deals.Add(deal);
        await db.SaveChangesAsync();

        var response = new DealResponse(
            deal.Id, deal.Title, deal.Amount, deal.CloseDate, deal.OwnerId,
            deal.PipelineId, deal.PipelineStageId, deal.CompanyId, deal.ContactId);
        return Results.Created($"/deals/{deal.Id}", response);
    }

    private static async Task<IResult> PatchDealStage(
        Guid id,
        PatchDealStageRequest req,
        CrmDbContext db,
        INotificationDispatcher dispatcher,
        CancellationToken ct)
    {
        var deal = await db.Deals.Where(d => d.Id == id).SingleOrDefaultAsync(ct);
        if (deal is null)
            return Results.NotFound();

        var stage = await db.PipelineStages.Where(s => s.Id == req.StageId).SingleOrDefaultAsync(ct);
        if (stage is null)
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["stageId"] = ["Stage not found."] },
                statusCode: StatusCodes.Status422UnprocessableEntity);

        var currentOwnerId = deal.OwnerId;
        try
        {
            deal.AdvanceTo(stage);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [ex.ParamName ?? "stage"] = [ex.Message] },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var stageChangeActivity = Activity.Create(
            ActivityType.StageChange,
            $"Stage changed to {stage.Name}",
            DateTime.UtcNow,
            dealId: deal.Id);
        db.Activities.Add(stageChangeActivity);

        var ownerChanged = req.OwnerId.HasValue && req.OwnerId.Value != currentOwnerId;
        if (ownerChanged)
        {
            try { deal.AssignOwner(req.OwnerId!.Value); }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { [ex.ParamName ?? "ownerId"] = [ex.Message] },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        }

        await db.SaveChangesAsync(ct);
        await dispatcher.DealStageChangedAsync(deal, stage, ct);
        if (ownerChanged)
            await dispatcher.DealAssignedAsync(deal, ct);

        return Results.Ok(new DealResponse(
            deal.Id, deal.Title, deal.Amount, deal.CloseDate, deal.OwnerId,
            deal.PipelineId, deal.PipelineStageId, deal.CompanyId, deal.ContactId));
    }
}
