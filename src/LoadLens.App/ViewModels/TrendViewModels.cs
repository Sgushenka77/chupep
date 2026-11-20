using System.Windows.Media;

namespace LoadLens.App.ViewModels;

public sealed class TrendStatViewModel
{
    public required string Label { get; init; }
    public required string NowText { get; init; }
    public required string AverageText { get; init; }
    public required string PeakText { get; init; }
    public required double NowPercent { get; init; }
    public required Brush Accent { get; init; }
}

public sealed class TrendEventViewModel
{
    public required string TimeText { get; init; }
    public required string Title { get; init; }
    public required Brush Accent { get; init; }
}
