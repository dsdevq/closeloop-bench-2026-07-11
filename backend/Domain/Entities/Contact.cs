using Domain.Common;

namespace Domain.Entities;

public sealed class Contact : Entity
{
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string? Phone { get; private set; }
    public Guid? CompanyId { get; private set; }
    public Guid OwnerId { get; private set; }

    private Contact() { }

    public static Contact Create(string name, string email, string? phone, Guid? companyId, Guid ownerId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Contact name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (ownerId == Guid.Empty)
            throw new ArgumentException("OwnerId must not be empty.", nameof(ownerId));

        var trimmedEmail = email.Trim();
        if (!IsValidEmail(trimmedEmail))
            throw new ArgumentException("Email must be a valid address.", nameof(email));

        return new Contact
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Email = trimmedEmail,
            Phone = phone,
            CompanyId = companyId,
            OwnerId = ownerId,
        };
    }

    public void AssignOwnerTo(Guid newOwnerId)
    {
        if (newOwnerId == Guid.Empty)
            throw new ArgumentException("OwnerId must not be empty.", nameof(newOwnerId));
        OwnerId = newOwnerId;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new System.Net.Mail.MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
