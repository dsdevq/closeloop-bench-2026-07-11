using Domain.Entities;
using Xunit;

namespace Tests.Domain;

public sealed class PipelineTests
{
    [Fact]
    public void Stages_AreReturnedInAscendingOrderByOrderField_RegardlessOfInsertionSequence()
    {
        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Stage C", order: 2);
        pipeline.AddStage("Stage A", order: 0);
        pipeline.AddStage("Stage B", order: 1);

        var orders = pipeline.Stages.Select(s => s.Order).ToArray();

        Assert.Equal(new[] { 0, 1, 2 }, orders);
        Assert.Equal(new[] { "Stage A", "Stage B", "Stage C" }, pipeline.Stages.Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Stages_OrderFieldIsUniqueAcrossAllStagesInPipeline()
    {
        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Prospecting", order: 0);
        pipeline.AddStage("Qualified", order: 1);
        pipeline.AddStage("Closing", order: 2);

        var orders = pipeline.Stages.Select(s => s.Order).ToArray();

        Assert.Equal(orders.Distinct().Count(), orders.Length);
    }

    [Fact]
    public void AddStage_WithDuplicateOrder_ThrowsArgumentException()
    {
        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Prospecting", order: 0);

        var ex = Assert.Throws<ArgumentException>(() => pipeline.AddStage("Qualified", order: 0));

        Assert.Equal("order", ex.ParamName);
    }

    [Fact]
    public void AddStage_WithNegativeOrder_ThrowsArgumentException()
    {
        var pipeline = Pipeline.Create("Sales");

        var ex = Assert.Throws<ArgumentException>(() => pipeline.AddStage("Prospecting", order: -1));

        Assert.Equal("order", ex.ParamName);
    }

    [Fact]
    public void AddStage_DuplicateOrderRejectedAfterMultipleStagesAlreadyAdded()
    {
        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Prospecting", order: 0);
        pipeline.AddStage("Qualified", order: 1);

        var ex = Assert.Throws<ArgumentException>(() => pipeline.AddStage("Closing", order: 1));

        Assert.Equal("order", ex.ParamName);
    }
}
