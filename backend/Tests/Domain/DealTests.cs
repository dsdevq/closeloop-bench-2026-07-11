using Domain.Entities;
using Xunit;

namespace Tests.Domain;

public sealed class DealTests
{
    private static (Pipeline pipeline, PipelineStage stage) MakePipelineWithStage()
    {
        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Prospecting", order: 0);
        return (pipeline, pipeline.Stages[0]);
    }

    [Fact]
    public void AdvanceTo_StageOutsidePipeline_Throws()
    {
        var (pipeline, stage) = MakePipelineWithStage();

        var otherPipeline = Pipeline.Create("Renewal");
        otherPipeline.AddStage("Review", order: 0);
        var foreignStage = otherPipeline.Stages[0];

        var deal = Deal.Create(500m, pipeline.Id, stage.Id);

        var ex = Assert.Throws<ArgumentException>(() => deal.AdvanceTo(foreignStage));

        Assert.Equal("stage", ex.ParamName);
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
    public void AdvanceTo_StageInSamePipeline_DoesNotChangePipelineId()
    {
        var (pipeline, firstStage) = MakePipelineWithStage();
        pipeline.AddStage("Qualified", order: 1);
        var secondStage = pipeline.Stages[1];

        var deal = Deal.Create(500m, pipeline.Id, firstStage.Id);

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
        var deal = Deal.Create(1000m, pipeline.Id, stages[0].Id);

        deal.AdvanceTo(stages[1]);
        Assert.Equal(stages[1].Id, deal.PipelineStageId);

        deal.AdvanceTo(stages[2]);
        Assert.Equal(stages[2].Id, deal.PipelineStageId);
    }
}
