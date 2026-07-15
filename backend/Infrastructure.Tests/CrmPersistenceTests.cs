using Domain.Entities;
using Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Infrastructure.Tests;

// Cross-layer round-trip tests: Domain entity factories + Infrastructure EF Core context.
// Infrastructure.Tests covers model metadata; these tests exercise actual save/query behaviour.
public sealed class CrmPersistenceTests
{
    // xUnit creates a new instance per test, so _dbName is unique per test but shared
    // between CreateContext() calls within the same test — enabling genuine round-trips.
    private readonly string _dbName = Guid.NewGuid().ToString();

    private CrmDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CrmDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options);

    [Fact]
    public async Task Contact_WithoutCompany_PersistsAndReloads()
    {
        var contact = Contact.Create("Alice Smith", "alice@example.com", null, null);

        await using var write = CreateContext();
        write.Contacts.Add(contact);
        await write.SaveChangesAsync();

        await using var read = CreateContext();
        var loaded = await read.Contacts.FindAsync(contact.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Alice Smith", loaded.Name);
        Assert.Equal("alice@example.com", loaded.Email);
        Assert.Null(loaded.CompanyId);
    }

    [Fact]
    public async Task Contact_WithCompanyId_FKValueRoundTrips()
    {
        var owner = Guid.NewGuid();
        var company = Company.Create("Acme Corp", "acme.com", "Technology", owner);
        var contact = Contact.Create("Bob Jones", "bob@acme.com", null, company.Id);

        await using var write = CreateContext();
        write.Companies.Add(company);
        write.Contacts.Add(contact);
        await write.SaveChangesAsync();

        await using var read = CreateContext();
        var loaded = await read.Contacts.FindAsync(contact.Id);

        Assert.NotNull(loaded);
        Assert.Equal(company.Id, loaded.CompanyId);
    }

    [Fact]
    public async Task Pipeline_WithStages_NavigationLoadsViaInclude()
    {
        var pipeline = Pipeline.Create("Sales Pipeline");
        pipeline.AddStage("Prospect", 0, 10);
        pipeline.AddStage("Qualified", 1, 40);
        pipeline.AddStage("Closed Won", 2, 100);

        await using var write = CreateContext();
        write.Pipelines.Add(pipeline);
        await write.SaveChangesAsync();

        await using var read = CreateContext();
        var loaded = await read.Pipelines
            .Include(p => p.Stages)
            .SingleAsync(p => p.Id == pipeline.Id);

        Assert.Equal("Sales Pipeline", loaded.Name);
        Assert.Equal(3, loaded.Stages.Count);
        // Stages are always sorted ascending by Order
        Assert.Equal("Prospect", loaded.Stages[0].Name);
        Assert.Equal("Qualified", loaded.Stages[1].Name);
        Assert.Equal("Closed Won", loaded.Stages[2].Name);
        Assert.Equal(40, loaded.Stages[1].WinProbability);
    }

    [Fact]
    public async Task Deal_LinksToExistingPipelineAndStage()
    {
        var pipeline = Pipeline.Create("Enterprise Pipeline");
        pipeline.AddStage("Discovery", 0, 20);

        var stage = pipeline.Stages[0];
        var deal = Deal.Create(15_000m, pipeline.Id, stage.Id);

        await using var write = CreateContext();
        write.Pipelines.Add(pipeline);
        write.Deals.Add(deal);
        await write.SaveChangesAsync();

        await using var read = CreateContext();
        var loaded = await read.Deals.FindAsync(deal.Id);

        Assert.NotNull(loaded);
        Assert.Equal(15_000m, loaded.Amount);
        Assert.Equal(pipeline.Id, loaded.PipelineId);
        Assert.Equal(stage.Id, loaded.PipelineStageId);
    }

    [Fact]
    public async Task Deal_AdvanceTo_UpdatesPipelineStageAndPersists()
    {
        var pipeline = Pipeline.Create("Standard Pipeline");
        pipeline.AddStage("Lead", 0);
        pipeline.AddStage("Proposal", 1);

        var leadStage = pipeline.Stages[0];
        var proposalStage = pipeline.Stages[1];
        var deal = Deal.Create(5_000m, pipeline.Id, leadStage.Id);

        // Advance before first save — AdvanceTo only mutates the FK, no DB interaction needed.
        deal.AdvanceTo(proposalStage);

        await using var write = CreateContext();
        write.Pipelines.Add(pipeline);
        write.Deals.Add(deal);
        await write.SaveChangesAsync();

        await using var read = CreateContext();
        var loaded = await read.Deals.FindAsync(deal.Id);

        Assert.NotNull(loaded);
        Assert.Equal(proposalStage.Id, loaded.PipelineStageId);
    }

    [Fact]
    public async Task Activity_AnchoredToContact_PersistsAndReloads()
    {
        var contact = Contact.Create("Carol White", "carol@example.com", null, null);
        var activity = Activity.Create(
            ActivityType.Call,
            "Introductory call",
            new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
            contactId: contact.Id);

        await using var write = CreateContext();
        write.Contacts.Add(contact);
        write.Activities.Add(activity);
        await write.SaveChangesAsync();

        await using var read = CreateContext();
        var loaded = await read.Activities.FindAsync(activity.Id);

        Assert.NotNull(loaded);
        Assert.Equal(ActivityType.Call, loaded.Type);
        Assert.Equal("Introductory call", loaded.Note);
        Assert.Equal(contact.Id, loaded.ContactId);
        Assert.Null(loaded.CompanyId);
        Assert.Null(loaded.DealId);
    }

    [Fact]
    public async Task Activity_AnchoredToCompany_PersistsAndReloads()
    {
        var owner = Guid.NewGuid();
        var company = Company.Create("Beta Ltd", null, "Finance", owner);
        var activity = Activity.Create(
            ActivityType.Email,
            "Sent proposal",
            new DateTime(2026, 2, 1, 14, 30, 0, DateTimeKind.Utc),
            companyId: company.Id);

        await using var write = CreateContext();
        write.Companies.Add(company);
        write.Activities.Add(activity);
        await write.SaveChangesAsync();

        await using var read = CreateContext();
        var loaded = await read.Activities.FindAsync(activity.Id);

        Assert.NotNull(loaded);
        Assert.Equal(ActivityType.Email, loaded.Type);
        Assert.Equal(company.Id, loaded.CompanyId);
        Assert.Null(loaded.ContactId);
        Assert.Null(loaded.DealId);
    }

    [Fact]
    public async Task Activity_AnchoredToDeal_PersistsAndReloads()
    {
        var pipeline = Pipeline.Create("Growth Pipeline");
        pipeline.AddStage("Intro", 0);
        var deal = Deal.Create(8_000m, pipeline.Id, pipeline.Stages[0].Id);
        var activity = Activity.Create(
            ActivityType.Meeting,
            "Kick-off meeting",
            new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc),
            dealId: deal.Id);

        await using var write = CreateContext();
        write.Pipelines.Add(pipeline);
        write.Deals.Add(deal);
        write.Activities.Add(activity);
        await write.SaveChangesAsync();

        await using var read = CreateContext();
        var loaded = await read.Activities.FindAsync(activity.Id);

        Assert.NotNull(loaded);
        Assert.Equal(ActivityType.Meeting, loaded.Type);
        Assert.Equal(deal.Id, loaded.DealId);
        Assert.Null(loaded.ContactId);
        Assert.Null(loaded.CompanyId);
    }
}
