// Audit: backend/Tests/Domain/PipelineTests.cs → Domain.Tests/Entities/PipelineTests.cs
//   Stages_AreReturnedInAscendingOrderByOrderField_RegardlessOfInsertionSequence  already covered: Stages_EnumeratesInAscendingOrderByOrder_RegardlessOfInsertionSequence
//   Stages_OrderFieldIsUniqueAcrossAllStagesInPipeline                            ported this PR: Stages_OrderFieldIsUniqueAcrossAllStagesInPipeline
//   AddStage_WithDuplicateOrder_ThrowsArgumentException                           ported this PR: AddStage_WithDuplicateOrder_ThrowsArgumentException
//   AddStage_WithNegativeOrder_ThrowsArgumentException                            already covered: AddStage_WithNegativeOrder_ThrowsArgumentException
//   AddStage_DuplicateOrderRejectedAfterMultipleStagesAlreadyAdded                ported this PR: AddStage_DuplicateOrderRejectedAfterMultipleStagesAlreadyAdded

using Domain.Entities;
using Xunit;

namespace Domain.Tests.Entities;

public sealed class PipelineTests
{
    [Fact]
    public void Create_WithValidName_ReturnsPopulatedPipeline()
    {
        var pipeline = Pipeline.Create("Sales Pipeline");

        Assert.Equal("Sales Pipeline", pipeline.Name);
        Assert.NotEqual(Guid.Empty, pipeline.Id);
        Assert.Empty(pipeline.Stages);
    }

    [Fact]
    public void Create_TrimsLeadingAndTrailingWhitespaceFromName()
    {
        var pipeline = Pipeline.Create("  Sales Pipeline  ");

        Assert.Equal("Sales Pipeline", pipeline.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ThrowsArgumentException(string? name)
    {
        var ex = Assert.Throws<ArgumentException>(() => Pipeline.Create(name!));

        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void Create_AssignsDistinctIdToEachInstance()
    {
        var a = Pipeline.Create("Alpha");
        var b = Pipeline.Create("Beta");

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Stages_EnumeratesInAscendingOrderByOrder_RegardlessOfInsertionSequence()
    {
        var pipeline = Pipeline.Create("Sales Pipeline");

        pipeline.AddStage("Stage C", order: 2);
        pipeline.AddStage("Stage A", order: 0);
        pipeline.AddStage("Stage B", order: 1);

        Assert.Equal(new[] { 0, 1, 2 }, pipeline.Stages.Select(s => s.Order).ToArray());
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
    public void AddStage_StartsEmpty_ThenAccumulatesStages()
    {
        var pipeline = Pipeline.Create("Sales Pipeline");

        Assert.Empty(pipeline.Stages);

        pipeline.AddStage("Prospecting", order: 0);
        pipeline.AddStage("Qualified", order: 1);

        Assert.Equal(2, pipeline.Stages.Count);
    }

    [Fact]
    public void AddStage_SetsCorrectPipelineIdOnEachStage()
    {
        var pipeline = Pipeline.Create("Sales Pipeline");

        pipeline.AddStage("Prospecting", order: 0);
        pipeline.AddStage("Proposal", order: 1);

        Assert.All(pipeline.Stages, s => Assert.Equal(pipeline.Id, s.PipelineId));
    }

    [Fact]
    public void AddStage_WithWinProbability_SetsWinProbabilityOnStage()
    {
        var pipeline = Pipeline.Create("Sales Pipeline");

        pipeline.AddStage("Closing", order: 0, winProbability: 80);

        Assert.Equal(80, pipeline.Stages.Single().WinProbability);
    }

    [Fact]
    public void AddStage_WithoutWinProbability_LeavesWinProbabilityNull()
    {
        var pipeline = Pipeline.Create("Sales Pipeline");

        pipeline.AddStage("Prospecting", order: 0);

        Assert.Null(pipeline.Stages.Single().WinProbability);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddStage_WithBlankName_ThrowsArgumentException(string? name)
    {
        var pipeline = Pipeline.Create("Sales Pipeline");

        var ex = Assert.Throws<ArgumentException>(() => pipeline.AddStage(name!, 0));

        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void AddStage_WithNegativeOrder_ThrowsArgumentException()
    {
        var pipeline = Pipeline.Create("Sales Pipeline");

        var ex = Assert.Throws<ArgumentException>(() => pipeline.AddStage("Stage", -1));

        Assert.Equal("order", ex.ParamName);
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
    public void AddStage_DuplicateOrderRejectedAfterMultipleStagesAlreadyAdded()
    {
        var pipeline = Pipeline.Create("Sales");
        pipeline.AddStage("Prospecting", order: 0);
        pipeline.AddStage("Qualified", order: 1);

        var ex = Assert.Throws<ArgumentException>(() => pipeline.AddStage("Closing", order: 1));

        Assert.Equal("order", ex.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void AddStage_WithOutOfRangeWinProbability_ThrowsArgumentException(int winProbability)
    {
        var pipeline = Pipeline.Create("Sales Pipeline");

        var ex = Assert.Throws<ArgumentException>(() => pipeline.AddStage("Stage", 0, winProbability));

        Assert.Equal("winProbability", ex.ParamName);
    }
}
