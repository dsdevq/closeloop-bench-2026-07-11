using Domain.Entities;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class ContactTests
{
    [Fact]
    public void Create_WithValidArguments_ReturnsPopulatedContact()
    {
        var companyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var contact = Contact.Create("Jane Doe", "jane@example.com", "+1-555-0100", companyId, ownerId);

        Assert.Equal("Jane Doe", contact.Name);
        Assert.Equal("jane@example.com", contact.Email);
        Assert.Equal("+1-555-0100", contact.Phone);
        Assert.Equal(companyId, contact.CompanyId);
        Assert.Equal(ownerId, contact.OwnerId);
        Assert.NotEqual(Guid.Empty, contact.Id);
    }

    [Fact]
    public void Create_WithoutCompanyId_SetsCompanyIdNull()
    {
        var contact = Contact.Create("Jane Doe", "jane@example.com", null, null, Guid.NewGuid());

        Assert.Null(contact.CompanyId);
    }

    [Fact]
    public void Create_WithCompanyId_SetsCompanyId()
    {
        var companyId = Guid.NewGuid();

        var contact = Contact.Create("Jane Doe", "jane@example.com", null, companyId, Guid.NewGuid());

        Assert.Equal(companyId, contact.CompanyId);
    }

    [Fact]
    public void Create_TrimsLeadingAndTrailingWhitespaceFromName()
    {
        var contact = Contact.Create("  Jane Doe  ", "jane@example.com", null, null, Guid.NewGuid());

        Assert.Equal("Jane Doe", contact.Name);
    }

    [Fact]
    public void Create_AllowsNullPhone()
    {
        var contact = Contact.Create("Jane Doe", "jane@example.com", null, null, Guid.NewGuid());

        Assert.Null(contact.Phone);
    }

    [Fact]
    public void Create_AssignsDistinctIdToEachInstance()
    {
        var a = Contact.Create("Alice", "alice@example.com", null, null, Guid.NewGuid());
        var b = Contact.Create("Bob", "bob@example.com", null, null, Guid.NewGuid());

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Create_WithEmptyOwnerId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Contact.Create("Jane Doe", "jane@example.com", null, null, Guid.Empty));

        Assert.Equal("ownerId", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ThrowsArgumentException(string? name)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Contact.Create(name!, "jane@example.com", null, null, Guid.NewGuid()));

        Assert.Equal("name", ex.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankEmail_ThrowsArgumentException(string? email)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Contact.Create("Jane Doe", email!, null, null, Guid.NewGuid()));

        Assert.Equal("email", ex.ParamName);
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("@nodomain")]
    [InlineData("noatsign.com")]
    public void Create_WithInvalidEmailFormat_ThrowsArgumentException(string email)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Contact.Create("Jane Doe", email, null, null, Guid.NewGuid()));

        Assert.Equal("email", ex.ParamName);
    }
}
