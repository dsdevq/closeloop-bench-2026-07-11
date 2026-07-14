using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Contacts;

public static class ContactsEndpoints
{
    public static IEndpointRouteBuilder MapContactsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/contacts");

        group.MapGet("/", ListContacts);
        group.MapGet("/{id:guid}", GetContact);
        group.MapPost("/", CreateContact);

        return app;
    }

    private static async Task<IResult> ListContacts(CrmDbContext db)
    {
        var contacts = await db.Contacts
            .Select(c => new ContactResponse(c.Id, c.Name, c.Email, c.Phone, c.CompanyId))
            .ToListAsync();
        return Results.Ok(contacts);
    }

    private static async Task<IResult> GetContact(Guid id, CrmDbContext db)
    {
        var contact = await db.Contacts
            .Where(c => c.Id == id)
            .Select(c => new ContactResponse(c.Id, c.Name, c.Email, c.Phone, c.CompanyId))
            .SingleOrDefaultAsync();

        return contact is null ? Results.NotFound() : Results.Ok(contact);
    }

    private static async Task<IResult> CreateContact(CreateContactRequest req, CrmDbContext db)
    {
        Contact contact;
        try
        {
            contact = Contact.Create(req.Name, req.Email, req.Phone, req.CompanyId);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var response = new ContactResponse(contact.Id, contact.Name, contact.Email, contact.Phone, contact.CompanyId);
        return Results.Created($"/contacts/{contact.Id}", response);
    }
}
