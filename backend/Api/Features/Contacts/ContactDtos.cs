namespace Api.Features.Contacts;

public sealed record ContactResponse(
    Guid Id,
    string Name,
    string Email,
    string? Phone,
    Guid? CompanyId);

public sealed record CreateContactRequest(
    string Name,
    string Email,
    string? Phone,
    Guid? CompanyId);
