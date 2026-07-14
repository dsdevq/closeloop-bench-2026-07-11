using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Companies;

public static class CompaniesEndpoints
{
    public static IEndpointRouteBuilder MapCompaniesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/companies");

        group.MapGet("/", ListCompanies);
        group.MapGet("/{id:guid}", GetCompany);
        group.MapPost("/", CreateCompany);

        return app;
    }

    private static async Task<IResult> ListCompanies(CrmDbContext db)
    {
        var companies = await db.Companies
            .Select(c => new CompanyResponse(c.Id, c.Name, c.Domain, c.Industry, c.OwnerId))
            .ToListAsync();
        return Results.Ok(companies);
    }

    private static async Task<IResult> GetCompany(Guid id, CrmDbContext db)
    {
        var company = await db.Companies
            .Where(c => c.Id == id)
            .Select(c => new CompanyResponse(c.Id, c.Name, c.Domain, c.Industry, c.OwnerId))
            .SingleOrDefaultAsync();

        return company is null ? Results.NotFound() : Results.Ok(company);
    }

    private static async Task<IResult> CreateCompany(CreateCompanyRequest req, CrmDbContext db)
    {
        Company company;
        try
        {
            company = Company.Create(req.Name, req.Domain, req.Industry, req.OwnerId);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var response = new CompanyResponse(company.Id, company.Name, company.Domain, company.Industry, company.OwnerId);
        return Results.Created($"/companies/{company.Id}", response);
    }
}
