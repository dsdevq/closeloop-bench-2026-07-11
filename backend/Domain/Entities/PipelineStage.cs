using Domain.Common;

namespace Domain.Entities;

public sealed class PipelineStage : Entity
{
    public Guid PipelineId { get; private set; }
    public string Name { get; private set; } = default!;
    public int Order { get; private set; }
    public int? WinProbability { get; private set; }

    private PipelineStage() { }

    internal static PipelineStage Create(Guid pipelineId, string name, int order, int? winProbability)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Stage name is required.", nameof(name));
        if (order < 0)
            throw new ArgumentException("Order must be non-negative.", nameof(order));
        if (winProbability is < 0 or > 100)
            throw new ArgumentException("WinProbability must be between 0 and 100.", nameof(winProbability));

        return new PipelineStage
        {
            Id = Guid.NewGuid(),
            PipelineId = pipelineId,
            Name = name.Trim(),
            Order = order,
            WinProbability = winProbability,
        };
    }
}
