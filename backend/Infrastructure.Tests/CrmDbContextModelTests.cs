using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Infrastructure.Tests;

public sealed class CrmDbContextModelTests
{
    private static CrmDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<CrmDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new CrmDbContext(options);
    }

    [Fact]
    public void OnModelCreating_BuildsWithoutThrowing()
    {
        // Accessing context.Model forces OnModelCreating to execute and the model
        // to be finalized — a wrong HasField name or invalid FK would throw here.
        using var ctx = BuildContext();
        var entityTypes = ctx.Model.GetEntityTypes().Select(e => e.ClrType).ToHashSet();

        Assert.Contains(typeof(Company), entityTypes);
        Assert.Contains(typeof(Contact), entityTypes);
        Assert.Contains(typeof(Pipeline), entityTypes);
        Assert.Contains(typeof(PipelineStage), entityTypes);
        Assert.Contains(typeof(Deal), entityTypes);
        Assert.Contains(typeof(Activity), entityTypes);
    }

    [Fact]
    public void OnModelCreating_PipelineStagesNavigationUsesBackingField()
    {
        using var ctx = BuildContext();
        var pipelineEntity = ctx.Model.FindEntityType(typeof(Pipeline))!;
        var nav = pipelineEntity.FindNavigation(nameof(Pipeline.Stages))!;

        Assert.NotNull(nav);
        Assert.Equal("_stages", nav.GetFieldName());
    }

    [Fact]
    public void OnModelCreating_ActivityAnchorFKsUseRestrict()
    {
        using var ctx = BuildContext();
        var activityEntity = ctx.Model.FindEntityType(typeof(Activity))!;
        var fkNames = new[] { "ContactId", "CompanyId", "DealId" };

        foreach (var fkName in fkNames)
        {
            var fk = activityEntity.GetForeignKeys()
                .Single(f => f.Properties.Any(p => p.Name == fkName));
            Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
        }
    }

    [Fact]
    public void OnModelCreating_PipelineStageToPipeline_CascadeDelete()
    {
        using var ctx = BuildContext();
        var pipelineStageEntity = ctx.Model.FindEntityType(typeof(PipelineStage))!;
        var fk = pipelineStageEntity.GetForeignKeys()
            .Single(f => f.Properties.Any(p => p.Name == "PipelineId"));

        Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior);
    }

    [Fact]
    public void OnModelCreating_IncludesNotificationEntity()
    {
        using var ctx = BuildContext();
        var entityTypes = ctx.Model.GetEntityTypes().Select(e => e.ClrType).ToHashSet();
        Assert.Contains(typeof(Notification), entityTypes);
    }

    [Fact]
    public void OnModelCreating_Notification_MappedToNotificationsTable()
    {
        using var ctx = BuildContext();
        var entity = ctx.Model.FindEntityType(typeof(Notification))!;
        Assert.Equal("notifications", entity.GetTableName());
    }

    [Fact]
    public void OnModelCreating_Notification_RecipientUserId_IsRequired()
    {
        using var ctx = BuildContext();
        var entity = ctx.Model.FindEntityType(typeof(Notification))!;
        var prop = entity.FindProperty(nameof(Notification.RecipientUserId))!;
        Assert.False(prop.IsNullable);
    }

    [Fact]
    public void OnModelCreating_Notification_Title_HasMaxLength500()
    {
        using var ctx = BuildContext();
        var entity = ctx.Model.FindEntityType(typeof(Notification))!;
        var prop = entity.FindProperty(nameof(Notification.Title))!;
        Assert.Equal(500, prop.GetMaxLength());
    }
}
