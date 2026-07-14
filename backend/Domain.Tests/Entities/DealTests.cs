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

    [Fact]
    public void Create_WithValidArguments_ReturnsPopulatedDeal()
    {
        var pipelineId = Guid.NewGuid();
        var stageId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        var deal = Deal.Create(1500m, pipelineId, stageId, companyId, contactId);

        Assert.NotEqual(Guid.Empty, deal.Id);
        Assert.Equal(1500m, deal.Amount);
        Assert.Equal(pipelineId, deal.PipelineId);
        Assert.Equal(stageId, deal.PipelineStageId);
        Assert.Equal(companyId, deal.CompanyId);
        Assert.Equal(contactId, deal.ContactId);
    }

    [Fact]
    public void Create_AllowsNullCompanyIdAndContactId()
    {
        var deal = Deal.Create(0m, Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(deal.CompanyId);
        Assert.Null(deal.ContactId);
    }

    [Fact]
    public void Create_AssignsDistinctIdToEachInstance()
    {
        var pId = Guid.NewGuid();
        var sId = Guid.NewGuid();

        var a = Deal.Create(100m, pId, sId);
        var b = Deal.Create(200m, pId, sId);

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Create_WithNegativeAmount_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Deal.Create(-1m, Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal("amount", ex.ParamName);
    }

    [Fact]
    public void Create_WithZeroAmount_Succeeds()
    {
        var deal = Deal.Create(0m, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(0m, deal.Amount);
    }

    [Fact]
    public void Create_WithEmptyPipelineId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Deal.Create(100m, Guid.Empty, Guid.NewGuid()));

        Assert.Equal("pipelineId", ex.ParamName);
    }

    [Fact]
    public void Create_WithEmptyPipelineStageId_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Deal.Create(100m, Guid.NewGuid(), Guid.Empty));

        Assert.Equal("pipelineStageId", ex.ParamName);
    }

    [Fact]
    public void AdvanceTo_StageInSamePipeline_UpdatesPipelineStageId()
    {
        var (pipeline, firstStage) = MakePipelineWithStage();
        pipeline.AddStage("Qualified", order: 1);
        var secondStage = pipeline.Stages[1];

        var deal = Deal.Create(500m, pipeline.Id, firstStage.Id);

        deal.AdvanceTo(secondStage);

        Assert.Equal(secondStage.Id, deal.PipelineStageId);
    }

    [Fact]
    public void AdvanceTo_StageFromDifferentPipeline_ThrowsArgumentException()
    {
        var (pipeline, stage) = MakePipelineWithStage();

        var otherPipeline = Pipeline.Create("Renewal");
        otherPipeline.AddStage("Review", order: 0);
        var otherStage = otherPipeline.Stages[0];

        var deal = Deal.Create(500m, pipeline.Id, stage.Id);

        var ex = Assert.Throws<ArgumentException>(() => deal.AdvanceTo(otherStage));

        Assert.Equal("stage", ex.ParamName);
    }
}
