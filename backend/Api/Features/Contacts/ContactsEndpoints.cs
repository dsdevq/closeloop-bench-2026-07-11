using Domain.Entities;
using Domain.Interfaces;
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
        group.MapPatch("/{id:guid}/owner", PatchContactOwner);

        return app;
    }

    private static async Task<IResult> ListContacts(CrmDbContext db)
    {
        var contacts = await db.Contacts
            .Select(c => new ContactResponse(c.Id, c.Name, c.Email, c.Phone, c.CompanyId, c.OwnerId))
            .ToListAsync();
        return Results.Ok(contacts);
    }

    private static async Task<IResult> GetContact(Guid id, CrmDbContext db)
    {
        var contact = await db.Contacts
            .Where(c => c.Id == id)
            .Select(c => new ContactResponse(c.Id, c.Name, c.Email, c.Phone, c.CompanyId, c.OwnerId))
            .SingleOrDefaultAsync();

        return contact is null ? Results.NotFound() : Results.Ok(contact);
    }

    private static async Task<IResult> CreateContact(CreateContactRequest req, CrmDbContext db)
    {
        Contact contact;
        try
        {
            contact = Contact.Create(req.Name, req.Email, req.Phone, req.CompanyId, req.OwnerId);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [ex.ParamName ?? "request"] = [ex.Message] },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var response = new ContactResponse(contact.Id, contact.Name, contact.Email, contact.Phone, contact.CompanyId, contact.OwnerId);
        return Results.Created($"/contacts/{contact.Id}", response);
    }

    private static async Task<IResult> PatchContactOwner(
        Guid id,
        PatchContactOwnerRequest req,
        CrmDbContext db,
        INotificationDispatcher dispatcher,
        CancellationToken ct)
    {
        var contact = await db.Contacts.Where(c => c.Id == id).SingleOrDefaultAsync(ct);
        if (contact is null)
            return Results.NotFound();

        if (req.OwnerId == contact.OwnerId)
            return Results.Ok(new ContactResponse(contact.Id, contact.Name, contact.Email, contact.Phone, contact.CompanyId, contact.OwnerId));

        try
        {
            contact.AssignOwnerTo(req.OwnerId);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]> { [ex.ParamName ?? "ownerId"] = [ex.Message] },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        await db.SaveChangesAsync(ct);
        await dispatcher.ContactAssignedAsync(contact, ct);

        return Results.Ok(new ContactResponse(contact.Id, contact.Name, contact.Email, contact.Phone, contact.CompanyId, contact.OwnerId));
    }
}
