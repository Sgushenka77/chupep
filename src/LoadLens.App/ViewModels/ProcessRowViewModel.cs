using System.Windows.Media;
using LoadLens.Core.Models;

namespace LoadLens.App.ViewModels;

public sealed class ProcessRowViewModel : ObservableObject
{
    private string _displayName = string.Empty;
    private string _initials = "?";
    private Brush _accent = Visuals.Brush(Visuals.Blue);
    private string _cpuText = "0%";
    private string _maxThreadText = "—";
    private string _gpuText = "—";
    private string _vramText = "—";
    private string _ramText = "—";
    private string _threadsText = "0";
    private bool _isTarget;
    private bool _hasWindow;
    private double _cpuPercent;
    private double _gpuPercent;
    private double _activityScore;

    public ProcessRowViewModel(int pid)
    {
        Pid = pid;
    }

    public int Pid { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => Set(ref _displayName, value);
    }

    public string Initials
    {
        get => _initials;
        private set => Set(ref _initials, value);
    }

    public Brush Accent
    {
        get => _accent;
        private set => Set(ref _accent, value);
    }

    public string CpuText
    {
        get => _cpuText;
        private set => Set(ref _cpuText, value);
    }

    public string MaxThreadText
    {
        get => _maxThreadText;
        private set => Set(ref _maxThreadText, value);
    }

    public string GpuText
    {
        get => _gpuText;
        private set => Set(ref _gpuText, value);
    }

    public string VramText
    {
        get => _vramText;
        private set => Set(ref _vramText, value);
    }

    public string RamText
    {
        get => _ramText;
        private set => Set(ref _ramText, value);
    }

    public string ThreadsText
    {
        get => _threadsText;
        private set => Set(ref _threadsText, value);
    }

    public bool IsTarget
    {
        get => _isTarget;
        private set => Set(ref _isTarget, value);
    }

    public bool HasWindow
    {
        get => _hasWindow;
        private set => Set(ref _hasWindow, value);
    }

    public double CpuPercent
    {
        get => _cpuPercent;
        private set => Set(ref _cpuPercent, value);
    }

    public double GpuPercent
    {
        get => _gpuPercent;
        private set => Set(ref _gpuPercent, value);
    }

    public double ActivityScore
    {
        get => _activityScore;
        private set => Set(ref _activityScore, value);
    }

    public void Update(ProcessMetrics process, bool isTarget)
    {
        DisplayName = process.DisplayName;
        Initials = Visuals.Initials(process.Name);
        Accent = Visuals.ProcessBrush(process.Name);
        CpuPercent = process.CpuPercent;
        GpuPercent = process.GpuPercent;
        ActivityScore = process.ActivityScore;
        CpuText = Visuals.Pct(process.CpuPercent);
        MaxThreadText = process.MaxThreadPercent >= 0.5
            ? Visuals.Pct(process.MaxThreadPercent)
            : "—";
        GpuText = process.GpuPercent >= 0.5 ? Visuals.Pct(process.GpuPercent) : "—";
        VramText = process.VramMb >= 8 ? Visuals.FormatMb(process.VramMb) : "—";
        RamText = Visuals.FormatMb(process.WorkingSetMb);
        ThreadsText = process.ThreadCount.ToString();
        HasWindow = process.HasWindow;
        IsTarget = isTarget;
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
}
