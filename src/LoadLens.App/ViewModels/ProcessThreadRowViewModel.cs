namespace LoadLens.App.ViewModels;

public sealed class ProcessThreadRowViewModel
{
    public required int ThreadId { get; init; }
    public required double CpuPercent { get; init; }
    public string ThreadText => $"#{ThreadId}";
    public string CpuText => $"{CpuPercent:0}% ядра";
    public double BarPercent => CpuPercent;
}
