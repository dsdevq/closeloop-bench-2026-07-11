using Domain.Entities;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class ActivityTests
{
    private static readonly DateTime _now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_AnchoredToContact_ReturnsPopulatedActivity()
    {
        var contactId = Guid.NewGuid();

        var activity = Activity.Create(ActivityType.Call, "Introduced product", _now, contactId: contactId);

        Assert.NotEqual(Guid.Empty, activity.Id);
        Assert.Equal(ActivityType.Call, activity.Type);
        Assert.Equal("Introduced product", activity.Note);
        Assert.Equal(_now, activity.OccurredAt);
        Assert.Equal(contactId, activity.ContactId);
        Assert.Null(activity.CompanyId);
        Assert.Null(activity.DealId);
    }

    [Fact]
    public void Create_AnchoredToCompany_ReturnsPopulatedActivity()
    {
        var companyId = Guid.NewGuid();

        var activity = Activity.Create(ActivityType.Email, "Sent proposal", _now, companyId: companyId);

        Assert.NotEqual(Guid.Empty, activity.Id);
        Assert.Equal(companyId, activity.CompanyId);
        Assert.Null(activity.ContactId);
        Assert.Null(activity.DealId);
    }

    [Fact]
    public void Create_AnchoredToDeal_ReturnsPopulatedActivity()
    {
        var dealId = Guid.NewGuid();

        var activity = Activity.Create(ActivityType.StageChange, "Deal moved to Closed Won", _now, dealId: dealId);

        Assert.NotEqual(Guid.Empty, activity.Id);
        Assert.Equal(dealId, activity.DealId);
        Assert.Null(activity.ContactId);
        Assert.Null(activity.CompanyId);
    }

    [Fact]
    public void Create_WithNoAnchor_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Activity.Create(ActivityType.Note, "Orphan activity", _now));
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Create_WithMultipleAnchors_ThrowsArgumentException(
        bool withContact, bool withCompany, bool withDeal)
    {
        var contactId = withContact ? Guid.NewGuid() : (Guid?)null;
        var companyId = withCompany ? Guid.NewGuid() : (Guid?)null;
        var dealId = withDeal ? Guid.NewGuid() : (Guid?)null;

        Assert.Throws<ArgumentException>(() =>
            Activity.Create(ActivityType.Meeting, "Over-anchored", _now,
                contactId, companyId, dealId));
    }

    [Fact]
    public void Create_AssignsDistinctIdToEachInstance()
    {
        var contactId = Guid.NewGuid();

        var a = Activity.Create(ActivityType.Note, "First note", _now, contactId: contactId);
        var b = Activity.Create(ActivityType.Note, "Second note", _now, contactId: contactId);

        Assert.NotEqual(a.Id, b.Id);
    }
}
