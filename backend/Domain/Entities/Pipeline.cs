using Domain.Common;

namespace Domain.Entities;

public sealed class Pipeline : Entity
{
    public string Name { get; private set; } = default!;

    private readonly List<PipelineStage> _stages = new();

    // Always sorted ascending by Order regardless of insertion sequence.
    public IReadOnlyList<PipelineStage> Stages => _stages.OrderBy(s => s.Order).ToList().AsReadOnly();

    private Pipeline() { }

    public static Pipeline Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Pipeline name is required.", nameof(name));

        return new Pipeline
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
        };
    }

    public void AddStage(string name, int order, int? winProbability = null)
    {
        if (_stages.Any(s => s.Order == order))
            throw new ArgumentException($"A stage with order {order} already exists in this pipeline.", nameof(order));

        _stages.Add(PipelineStage.Create(Id, name, order, winProbability));
    }
}
