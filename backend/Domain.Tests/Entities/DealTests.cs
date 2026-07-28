// Audit: backend/Tests/Domain/DealTests.cs → Domain.Tests/Entities/DealTests.cs
//   AdvanceTo_StageOutsidePipeline_Throws              already covered: AdvanceTo_StageFromDifferentPipeline_ThrowsArgumentException
//   AdvanceTo_StageInSamePipeline_UpdatesPipelineStageId  already covered: AdvanceTo_StageInSamePipeline_UpdatesPipelineStageId
//   AdvanceTo_StageInSamePipeline_DoesNotChangePipelineId ported this PR: AdvanceTo_StageInSamePipeline_DoesNotChangePipelineId
//   AdvanceTo_CanAdvanceMultipleTimes_WithinSamePipeline  ported this PR: AdvanceTo_CanAdvanceMultipleTimes_WithinSamePipeline

using Domain.Entities;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class DealTests
{
    private static (Pipeline pipeline, PipelineStage stage) MakePipelineWithStage()
    {
        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Prospecting", order: 0);
        var stage = pipeline.Stages[0];
        return (pipeline, stage);
    }

    private static Deal MakeDeal(Pipeline pipeline, PipelineStage stage, decimal amount = 500m)
        => Deal.Create("Test Deal", amount, Guid.NewGuid(), pipeline.Id, stage.Id);

    [Fact]
    public void Create_WithValidArguments_ReturnsPopulatedDeal()
    {
        var ownerId = Guid.NewGuid();
        var pipelineId = Guid.NewGuid();
        var stageId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var closeDate = new DateOnly(2026, 12, 31);

        var deal = Deal.Create("Enterprise License", 1500m, ownerId, pipelineId, stageId, closeDate, companyId, contactId);

        Assert.NotEqual(Guid.Empty, deal.Id);
        Assert.Equal("Enterprise License", deal.Title);
        Assert.Equal(1500m, deal.Amount);
        Assert.Equal(ownerId, deal.OwnerId);
        Assert.Equal(pipelineId, deal.PipelineId);
        Assert.Equal(stageId, deal.PipelineStageId);
        Assert.Equal(closeDate, deal.CloseDate);
        Assert.Equal(companyId, deal.CompanyId);
        Assert.Equal(contactId, deal.ContactId);
    }

    [Fact]
    public void Create_AllowsNullCompanyIdAndContactIdAndCloseDate()
    {
        var deal = Deal.Create("Simple Deal", 0m, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(deal.CompanyId);
        Assert.Null(deal.ContactId);
        Assert.Null(deal.CloseDate);
    }

    [Fact]
    public void Create_AssignsDistinctIdToEachInstance()
    {
        var pId = Guid.NewGuid();
        var sId = Guid.NewGuid();
        var oId = Guid.NewGuid();

        var a = Deal.Create("Deal A", 100m, oId, pId, sId);
        var b = Deal.Create("Deal B", 200m, oId, pId, sId);

        Assert.NotEqual(a.Id, b.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankTitle_ThrowsArgumentException(string? title)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Deal.Create(title!, 100m, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal("title", ex.ParamName);
    }

    [Fact]
    public void Create_TrimsTitleWhitespace()
    {
        var deal = Deal.Create("  My Deal  ", 0m, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("My Deal", deal.Title);
    }

    [Fact]
    public void Create_WithNegativeAmount_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Deal.Create("Deal", -1m, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal("amount", ex.ParamName);
    }

    [Fact]
    public void Create_WithZeroAmount_Succeeds()
    {
        var deal = Deal.Create("Free Deal", 0m, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(0m, deal.Amount);
    }

    [Fact]
    public void Create_WithEmptyOwnerId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Deal.Create("Deal", 100m, Guid.Empty, Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal("ownerId", ex.ParamName);
    }

    [Fact]
    public void Create_WithEmptyPipelineId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Deal.Create("Deal", 100m, Guid.NewGuid(), Guid.Empty, Guid.NewGuid()));

        Assert.Equal("pipelineId", ex.ParamName);
    }

    [Fact]
    public void Create_WithEmptyPipelineStageId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Deal.Create("Deal", 100m, Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));

        Assert.Equal("pipelineStageId", ex.ParamName);
    }

    [Fact]
    public void AdvanceTo_StageInSamePipeline_UpdatesPipelineStageId()
    {
        var (pipeline, firstStage) = MakePipelineWithStage();
        pipeline.AddStage("Qualified", order: 1);
        var secondStage = pipeline.Stages[1];

        var deal = MakeDeal(pipeline, firstStage);

        deal.AdvanceTo(secondStage);

        Assert.Equal(secondStage.Id, deal.PipelineStageId);
    }

    [Fact]
    public void AdvanceTo_StageInSamePipeline_DoesNotChangePipelineId()
    {
        var (pipeline, firstStage) = MakePipelineWithStage();
        pipeline.AddStage("Qualified", order: 1);
        var secondStage = pipeline.Stages[1];

        var deal = MakeDeal(pipeline, firstStage);

        deal.AdvanceTo(secondStage);

        Assert.Equal(pipeline.Id, deal.PipelineId);
    }

    [Fact]
    public void AdvanceTo_CanAdvanceMultipleTimes_WithinSamePipeline()
    {
        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Prospecting", order: 0);
        pipeline.AddStage("Qualified", order: 1);
        pipeline.AddStage("Closing", order: 2);

        var stages = pipeline.Stages;
        var deal = Deal.Create("Big Deal", 1000m, Guid.NewGuid(), pipeline.Id, stages[0].Id);

        deal.AdvanceTo(stages[1]);
        Assert.Equal(stages[1].Id, deal.PipelineStageId);

        deal.AdvanceTo(stages[2]);
        Assert.Equal(stages[2].Id, deal.PipelineStageId);
    }

    [Fact]
    public void AdvanceTo_StageFromDifferentPipeline_ThrowsArgumentException()
    {
        var (pipeline, stage) = MakePipelineWithStage();

        var otherPipeline = Pipeline.Create("Renewal");
        otherPipeline.AddStage("Review", order: 0);
        var otherStage = otherPipeline.Stages[0];

        var deal = MakeDeal(pipeline, stage);

        var ex = Assert.Throws<ArgumentException>(() => deal.AdvanceTo(otherStage));

        Assert.Equal("stage", ex.ParamName);
    }

    [Fact]
    public void AdvanceTo_StageOutsidePipeline_Throws()
    {
        var (pipeline, stage) = MakePipelineWithStage();

        var otherPipeline = Pipeline.Create("Renewal");
        otherPipeline.AddStage("Review", order: 0);
        var foreignStage = otherPipeline.Stages[0];

        var deal = MakeDeal(pipeline, stage);

        var ex = Assert.Throws<ArgumentException>(() => deal.AdvanceTo(foreignStage));

        Assert.Equal("stage", ex.ParamName);
    }
}
