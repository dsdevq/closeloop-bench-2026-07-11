namespace Api.Features.Companies;

public sealed record CompanyResponse(
    Guid Id,
    string Name,
    string? Domain,
    string? Industry,
    Guid OwnerId);

public sealed record CreateCompanyRequest(
    string Name,
    string? Domain,
    string? Industry,
    Guid OwnerId);
