namespace Api.Features.Search;

public sealed record SearchContactResult(Guid Id, string Name, string Email);
public sealed record SearchCompanyResult(Guid Id, string Name, string? Domain);
public sealed record SearchDealResult(Guid Id, string Title, decimal Amount);

public sealed record SearchResponse(
    IReadOnlyList<SearchContactResult> Contacts,
    IReadOnlyList<SearchCompanyResult> Companies,
    IReadOnlyList<SearchDealResult> Deals,
    int TotalHits);
