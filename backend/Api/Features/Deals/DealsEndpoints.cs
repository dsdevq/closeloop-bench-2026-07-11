using Domain.Entities;
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

        return app;
    }

    private static async Task<IResult> ListDeals(CrmDbContext db)
    {
        var deals = await db.Deals
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
}
