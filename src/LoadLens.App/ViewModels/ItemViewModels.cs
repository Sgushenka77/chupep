using System.Windows.Media;

namespace LoadLens.App.ViewModels;

public sealed class EvidenceChipViewModel
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public string? Detail { get; init; }
}

public sealed class ActionItemViewModel
{
    public required int Number { get; init; }
    public required string Text { get; init; }
}

public sealed class FindingRowViewModel
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Badge { get; init; }
    public required Brush Accent { get; init; }
    public required Brush Tint { get; init; }
    public required double Confidence { get; init; }
}
