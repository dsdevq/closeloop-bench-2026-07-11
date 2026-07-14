using Domain.Entities;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class CompanyTests
{
    [Fact]
    public void Create_WithValidArguments_ReturnsPopulatedCompany()
    {
        var ownerId = Guid.NewGuid();

        var company = Company.Create("Acme Corp", "acme.com", "Technology", ownerId);

        Assert.Equal("Acme Corp", company.Name);
        Assert.Equal("acme.com", company.Domain);
        Assert.Equal("Technology", company.Industry);
        Assert.Equal(ownerId, company.OwnerId);
        Assert.NotEqual(Guid.Empty, company.Id);
    }

    [Fact]
    public void Create_TrimsLeadingAndTrailingWhitespaceFromName()
    {
        var company = Company.Create("  Acme Corp  ", null, null, Guid.NewGuid());

        Assert.Equal("Acme Corp", company.Name);
    }

    [Fact]
    public void Create_AllowsNullDomainAndIndustry()
    {
        var company = Company.Create("Acme Corp", null, null, Guid.NewGuid());

        Assert.Null(company.Domain);
        Assert.Null(company.Industry);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ThrowsArgumentException(string? name)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Company.Create(name!, null, null, Guid.NewGuid()));

        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void Create_WithEmptyOwnerId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Company.Create("Acme Corp", null, null, Guid.Empty));

        Assert.Equal("ownerId", ex.ParamName);
    }

    [Fact]
    public void Create_AssignsDistinctIdToEachInstance()
    {
        var a = Company.Create("Alpha", null, null, Guid.NewGuid());
        var b = Company.Create("Beta", null, null, Guid.NewGuid());

        Assert.NotEqual(a.Id, b.Id);
    }
}
