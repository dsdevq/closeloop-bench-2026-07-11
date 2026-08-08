using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Search;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/search", Search);
        return app;
    }

    private static async Task<IResult> Search(string? q, CrmDbContext db)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { ["q"] = ["Search query is required."] },
                statusCode: StatusCodes.Status422UnprocessableEntity);

        var term = q.Trim().ToLower();

        // Uncapped counts per type — used to compute TotalHits so the client knows
        // how many records truly match and can offer a "see all N {type}s" affordance.
        var contactCount = await db.Contacts
            .CountAsync(c => c.Name.ToLower().Contains(term) || c.Email.ToLower().Contains(term));
        var companyCount = await db.Companies
            .CountAsync(c => c.Name.ToLower().Contains(term) || (c.Domain != null && c.Domain.ToLower().Contains(term)));
        var dealCount = await db.Deals
            .CountAsync(d => d.Title.ToLower().Contains(term));

        var contacts = await db.Contacts
            .Where(c => c.Name.ToLower().Contains(term) || c.Email.ToLower().Contains(term))
            .OrderBy(c => c.Name)
            .Take(10)
            .Select(c => new SearchContactResult(c.Id, c.Name, c.Email))
            .ToListAsync();

        var companies = await db.Companies
            .Where(c => c.Name.ToLower().Contains(term) || (c.Domain != null && c.Domain.ToLower().Contains(term)))
            .OrderBy(c => c.Name)
            .Take(10)
            .Select(c => new SearchCompanyResult(c.Id, c.Name, c.Domain))
            .ToListAsync();

        var deals = await db.Deals
            .Where(d => d.Title.ToLower().Contains(term))
            .OrderBy(d => d.Title)
            .Take(10)
            .Select(d => new SearchDealResult(d.Id, d.Title, d.Amount))
            .ToListAsync();

        return Results.Ok(new SearchResponse(contacts, companies, deals, contactCount + companyCount + dealCount));
    }
}
