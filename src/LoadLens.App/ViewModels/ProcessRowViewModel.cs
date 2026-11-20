using System.Collections.ObjectModel;
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
    private string _diskText = "—";
    private string _diskReadText = "0.0 МБ/с";
    private string _diskWriteText = "0.0 МБ/с";
    private string _threadsText = "0";
    private string _windowText = "фон";
    private bool _isTarget;
    private bool _hasWindow;
    private bool _hasThreads;
    private double _cpuPercent;
    private double _gpuPercent;
    private double _activityScore;

    public ProcessRowViewModel(int pid)
    {
        Pid = pid;
    }

    public int Pid { get; }

    public ProcessMetrics? Model { get; private set; }

    public ObservableCollection<ProcessThreadRowViewModel> TopThreads { get; } = new();

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

    public string DiskText
    {
        get => _diskText;
        private set => Set(ref _diskText, value);
    }

    public string DiskReadText
    {
        get => _diskReadText;
        private set => Set(ref _diskReadText, value);
    }

    public string DiskWriteText
    {
        get => _diskWriteText;
        private set => Set(ref _diskWriteText, value);
    }

    public string WindowText
    {
        get => _windowText;
        private set => Set(ref _windowText, value);
    }

    public bool HasThreads
    {
        get => _hasThreads;
        private set => Set(ref _hasThreads, value);
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
        Model = process;
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
        DiskText = process.DiskMbps >= 0.05 ? $"{process.DiskMbps:0.0} МБ/с" : "—";
        DiskReadText = $"{process.DiskReadMbps:0.0} МБ/с";
        DiskWriteText = $"{process.DiskWriteMbps:0.0} МБ/с";
        ThreadsText = process.ThreadCount.ToString();
        WindowText = process.HasWindow ? "окно" : "фон";
        HasWindow = process.HasWindow;
        IsTarget = isTarget;

        TopThreads.Clear();
        foreach (var thread in process.Threads.Take(5))
        {
            TopThreads.Add(new ProcessThreadRowViewModel
            {
                ThreadId = thread.ThreadId,
                CpuPercent = thread.CpuPercent
            });
        }

        HasThreads = TopThreads.Count > 0;
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
}
