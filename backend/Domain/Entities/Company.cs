using Domain.Common;

namespace Domain.Entities;

public sealed class Company : Entity
{
    public string Name { get; private set; } = default!;
    public string? Domain { get; private set; }
    public string? Industry { get; private set; }
    public Guid OwnerId { get; private set; }

    private Company() { }

    public static Company Create(string name, string? domain, string? industry, Guid ownerId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Company name is required.", nameof(name));
        if (ownerId == Guid.Empty)
            throw new ArgumentException("OwnerId must not be empty.", nameof(ownerId));

        return new Company
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Domain = domain,
            Industry = industry,
            OwnerId = ownerId,
        };
    }
}
