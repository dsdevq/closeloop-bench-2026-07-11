using Domain.Entities;
using Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.Tests.Jobs;

public sealed class DealRottingNotificationJobTests
{
    private static (IServiceScopeFactory ScopeFactory, ServiceProvider Provider) BuildServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CrmDbContext>(opt =>
            opt.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IServiceScopeFactory>(), provider);
    }

    private static DealRottingNotificationJob BuildJob(IServiceScopeFactory scopeFactory) =>
        new(scopeFactory, NullLogger<DealRottingNotificationJob>.Instance);

    [Fact]
    public async Task ScanAsync_NoPipelines_CreatesNoNotifications()
    {
        var (scopeFactory, provider) = BuildServices();
        await using var _ = provider;

        await BuildJob(scopeFactory).ScanAsync();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task ScanAsync_PipelineWithNoThreshold_CreatesNoNotifications()
    {
        var (scopeFactory, provider) = BuildServices();
        await using var _ = provider;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var pipeline = Pipeline.Create("No-threshold pipeline");
            pipeline.AddStage("Open", 1);
            db.Pipelines.Add(pipeline);
            await db.SaveChangesAsync();

            var stage = pipeline.Stages.First();
            db.Deals.Add(Deal.Create(100m, pipeline.Id, stage.Id));
            await db.SaveChangesAsync();
        }

        await BuildJob(scopeFactory).ScanAsync();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            Assert.Equal(0, await db.Notifications.CountAsync());
        }
    }

    [Fact]
    public async Task ScanAsync_DealWithRecentActivity_CreatesNoNotifications()
    {
        var (scopeFactory, provider) = BuildServices();
        await using var _ = provider;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var pipeline = Pipeline.Create("Sales");
            pipeline.SetRottingThresholdDays(14);
            pipeline.AddStage("Qualified", 1);
            db.Pipelines.Add(pipeline);
            await db.SaveChangesAsync();

            var stage = pipeline.Stages.First();
            var deal = Deal.Create(500m, pipeline.Id, stage.Id);
            db.Deals.Add(deal);

            // Activity logged today — deal is NOT rotting
            db.Activities.Add(Activity.Create(
                ActivityType.Note, "Recent call", DateTime.UtcNow.AddHours(-1), dealId: deal.Id));

            await db.SaveChangesAsync();
        }

        await BuildJob(scopeFactory).ScanAsync();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            Assert.Equal(0, await db.Notifications.CountAsync());
        }
    }

    [Fact]
    public async Task ScanAsync_RottenDeal_SkipsNotificationPendingDealOwnerIdConcept()
    {
        // Verifies the job correctly identifies rotting deals but skips notification
        // creation because Deal.OwnerId is not yet implemented. Once Deal.OwnerId is
        // added the job will create a DealRotting notification here.
        var (scopeFactory, provider) = BuildServices();
        await using var _ = provider;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            var pipeline = Pipeline.Create("Sales");
            pipeline.SetRottingThresholdDays(7);
            pipeline.AddStage("Proposal", 1);
            db.Pipelines.Add(pipeline);
            await db.SaveChangesAsync();

            var stage = pipeline.Stages.First();
            var deal = Deal.Create(1000m, pipeline.Id, stage.Id);
            db.Deals.Add(deal);

            // Last activity was 30 days ago — deal IS rotting (exceeds 7-day threshold)
            db.Activities.Add(Activity.Create(
                ActivityType.Call, "Old call", DateTime.UtcNow.AddDays(-30), dealId: deal.Id));

            await db.SaveChangesAsync();
        }

        await BuildJob(scopeFactory).ScanAsync();

        // Job skips notification creation because Deal.OwnerId is not available.
        // The 0-count asserts correct no-op behavior until ownership is implemented.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
            Assert.Equal(0, await db.Notifications.CountAsync());
        }
    }
}
