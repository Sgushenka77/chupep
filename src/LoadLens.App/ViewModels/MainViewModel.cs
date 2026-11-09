using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using LoadLens.Core.Diagnostics;
using LoadLens.Core.Models;
using LoadLens.Core.Reports;
using Microsoft.Win32;

namespace LoadLens.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int WarmupSamples = 5;

    private static readonly string AppVersion =
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private readonly MonitoringService _service;
    private readonly Dispatcher _dispatcher;
    private readonly ObservableCollection<ProcessRowViewModel> _processes = new();
    private readonly Dictionary<int, ProcessRowViewModel> _rowsByPid = new();
    private readonly ObservableCollection<ProcessRowViewModel> _topProcesses = new();
    private readonly ObservableCollection<EvidenceChipViewModel> _evidence = new();
    private readonly ObservableCollection<ActionItemViewModel> _primaryActions = new();
    private readonly ObservableCollection<FindingRowViewModel> _secondaryFindings = new();
    private readonly RelayCommand _togglePauseCommand;
    private readonly RelayCommand _saveReportCommand;
    private readonly RelayCommand _openLastReportCommand;
    private readonly RelayCommand _openReportsFolderCommand;
    private readonly RelayCommand _showProcessesCommand;
    private readonly RelayCommand _showReportCommand;

    private MonitoringUpdate? _lastUpdate;
    private bool _disposed;
    private string _selectedPage = "Overview";
    private string _pageTitle = "Почему лагает?";
    private string _pageSubtitle = "Что именно ограничивает систему прямо сейчас";
    private string _searchText = string.Empty;
    private string _statusText = "ЗАПУСК";
    private string _updatedText = "ожидание данных";
    private string _pauseButtonText = "Пауза";
    private string _pauseGlyph = "II";
    private bool _isPaused;
    private Brush _statusBrush = Visuals.Brush(Visuals.Amber);
    private string _verdictTitle = "Запускаю монитор";
    private string _verdictSummary =
        "Считываю нагрузку, потоки и память. Первый вердикт появится через несколько секунд.";
    private string _severityText = "ПОДГОТОВКА";
    private string _confidenceText = "Наблюдение начнётся сразу после запуска";
    private Brush _severityBrush = Visuals.Brush(Visuals.Blue);
    private Brush _severityTintBrush = Visuals.SeverityTint(Severity.Info);
    private Brush _heroBrush = Visuals.HeroGradient(Severity.Info);
    private bool _hasTarget;
    private string _targetName = string.Empty;
    private string _targetPidText = string.Empty;
    private string _targetInitials = "?";
    private Brush _targetBrush = Visuals.Brush(Visuals.Blue);
    private string _targetThreadValue = "—";
    private string _targetThreadLabel = "самый горячий поток";
    private double _targetThreadPercent;
    private string _targetNote = string.Empty;
    private string _targetStats = string.Empty;
    private string _healthyTitle = "Ресурсы в норме";
    private string _healthyText = "Если лаги остались, сохраните отчёт в момент проблемы.";
    private string _secondaryCountText = "проверок";
    private bool _hasSecondaryFindings;
    private string _processCountText = "0 процессов";
    private string _visibleProcessCountText = "0 из 0";
    private string _sessionDurationText = "0 с";
    private string _samplesText = "0";
    private string _avgCpuText = "—";
    private string _avgGpuText = "—";
    private string _avgRamText = "—";
    private string _peakCpuText = "—";
    private string _peakGpuText = "—";
    private string _peakRamText = "—";
    private string _peakVramText = "—";
    private string _reportPathText = "Отчёты сохраняются в «Документы\\LoadLens»";
    private string _lastReportPath = string.Empty;
    private bool _hasReport;
    private string _lastError = string.Empty;
    private string _hardwareCpuName = "—";
    private string _hardwareCpuDetail = string.Empty;
    private string _hardwareGpuName = "—";
    private string _hardwareGpuDetail = string.Empty;
    private string _hardwareRam = "—";
    private string _hardwareOs = "—";
    private string _hardwarePrivileges = "—";
    private string _sessionStartedText = string.Empty;
    private string _verdictKind = "Наблюдение";

    public MainViewModel()
    {
        _dispatcher = Application.Current.Dispatcher;
        ProcessRows = _processes;
        ProcessView = CollectionViewSource.GetDefaultView(_processes);
        ProcessView.Filter = FilterProcess;
        ProcessView.SortDescriptions.Add(
            new SortDescription(nameof(ProcessRowViewModel.CpuPercent), ListSortDirection.Descending));
        if (ProcessView is ICollectionViewLiveShaping liveShaping && liveShaping.CanChangeLiveSorting)
        {
            liveShaping.LiveSortingProperties.Add(nameof(ProcessRowViewModel.CpuPercent));
            liveShaping.IsLiveSorting = true;
        }

        var idle = Visuals.Brush("#5D6E82");
        MetricCards = new ObservableCollection<MetricCardViewModel>
        {
            new("CPU", Visuals.Brush(Visuals.Blue), idle),
            new("GPU", Visuals.Brush(Visuals.Purple), idle),
            new("RAM", Visuals.Brush(Visuals.Green), idle),
            new("VRAM", Visuals.Brush(Visuals.Orange), idle),
            new("Диск", Visuals.Brush(Visuals.Cyan), idle)
        };

        _togglePauseCommand = new RelayCommand(TogglePause);
        _saveReportCommand = new RelayCommand(SaveReport, () => _lastUpdate is not null);
        _openLastReportCommand = new RelayCommand(OpenLastReport, () => HasReport);
        _openReportsFolderCommand = new RelayCommand(OpenReportsFolder);
        _showProcessesCommand = new RelayCommand(() => SelectedPage = "Processes");
        _showReportCommand = new RelayCommand(() => SelectedPage = "Report");

        _service = new MonitoringService(AppVersion);
        _service.Updated += OnUpdated;
        _service.Error += OnError;

        FillHardware(_service.Info);
        UpdateStatus();

        _service.Start();
    }

    public ObservableCollection<MetricCardViewModel> MetricCards { get; }

    public ObservableCollection<ProcessRowViewModel> ProcessRows { get; }

    public ICollectionView ProcessView { get; }

    public ObservableCollection<ProcessRowViewModel> TopProcesses => _topProcesses;

    public ObservableCollection<EvidenceChipViewModel> Evidence => _evidence;

    public ObservableCollection<ActionItemViewModel> PrimaryActions => _primaryActions;

    public ObservableCollection<FindingRowViewModel> SecondaryFindings => _secondaryFindings;

    public RelayCommand TogglePauseCommand => _togglePauseCommand;

    public RelayCommand SaveReportCommand => _saveReportCommand;

    public RelayCommand OpenLastReportCommand => _openLastReportCommand;

    public RelayCommand OpenReportsFolderCommand => _openReportsFolderCommand;

    public RelayCommand ShowProcessesCommand => _showProcessesCommand;

    public RelayCommand ShowReportCommand => _showReportCommand;

    public string SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (!Set(ref _selectedPage, value))
            {
                return;
            }

            (PageTitle, PageSubtitle) = value switch
            {
                "Processes" => ("Процессы", "Детализация нагрузки по CPU, GPU и памяти"),
                "Report" => ("Отчёт", "Снимок состояния, доказательства и рекомендации"),
                _ => ("Почему лагает?", "Что именно ограничивает систему прямо сейчас")
            };
        }
    }

    public string PageTitle
    {
        get => _pageTitle;
        private set => Set(ref _pageTitle, value);
    }

    public string PageSubtitle
    {
        get => _pageSubtitle;
        private set => Set(ref _pageSubtitle, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                ProcessView.Refresh();
                UpdateVisibleCount();
            }
        }
    }

    public string ProcessCountText
    {
        get => _processCountText;
        private set => Set(ref _processCountText, value);
    }

    public string VisibleProcessCountText
    {
        get => _visibleProcessCountText;
        private set => Set(ref _visibleProcessCountText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string UpdatedText
    {
        get => _updatedText;
        private set => Set(ref _updatedText, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => Set(ref _statusBrush, value);
    }

    public string PauseButtonText
    {
        get => _pauseButtonText;
        private set => Set(ref _pauseButtonText, value);
    }

    public string PauseGlyph
    {
        get => _pauseGlyph;
        private set => Set(ref _pauseGlyph, value);
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set => Set(ref _isPaused, value);
    }

    public string VerdictTitle
    {
        get => _verdictTitle;
        private set => Set(ref _verdictTitle, value);
    }

    public string VerdictSummary
    {
        get => _verdictSummary;
        private set => Set(ref _verdictSummary, value);
    }

    public string SeverityText
    {
        get => _severityText;
        private set => Set(ref _severityText, value);
    }

    public string ConfidenceText
    {
        get => _confidenceText;
        private set => Set(ref _confidenceText, value);
    }

    public Brush SeverityBrush
    {
        get => _severityBrush;
        private set => Set(ref _severityBrush, value);
    }

    public Brush SeverityTintBrush
    {
        get => _severityTintBrush;
        private set => Set(ref _severityTintBrush, value);
    }

    public Brush HeroBrush
    {
        get => _heroBrush;
        private set => Set(ref _heroBrush, value);
    }

    public bool HasTarget
    {
        get => _hasTarget;
        private set
        {
            if (Set(ref _hasTarget, value))
            {
                Raise(nameof(ShowHealthyState));
            }
        }
    }

    public bool ShowHealthyState => !HasTarget;

    public string TargetName
    {
        get => _targetName;
        private set => Set(ref _targetName, value);
    }

    public string TargetPidText
    {
        get => _targetPidText;
        private set => Set(ref _targetPidText, value);
    }

    public string TargetInitials
    {
        get => _targetInitials;
        private set => Set(ref _targetInitials, value);
    }

    public Brush TargetBrush
    {
        get => _targetBrush;
        private set => Set(ref _targetBrush, value);
    }

    public string TargetThreadValue
    {
        get => _targetThreadValue;
        private set => Set(ref _targetThreadValue, value);
    }

    public string TargetThreadLabel
    {
        get => _targetThreadLabel;
        private set => Set(ref _targetThreadLabel, value);
    }

    public double TargetThreadPercent
    {
        get => _targetThreadPercent;
        private set => Set(ref _targetThreadPercent, value);
    }

    public string TargetNote
    {
        get => _targetNote;
        private set => Set(ref _targetNote, value);
    }

    public string TargetStats
    {
        get => _targetStats;
        private set => Set(ref _targetStats, value);
    }

    public string HealthyTitle
    {
        get => _healthyTitle;
        private set => Set(ref _healthyTitle, value);
    }

    public string HealthyText
    {
        get => _healthyText;
        private set => Set(ref _healthyText, value);
    }

    public string SecondaryCountText
    {
        get => _secondaryCountText;
        private set => Set(ref _secondaryCountText, value);
    }

    public bool HasSecondaryFindings
    {
        get => _hasSecondaryFindings;
        private set
        {
            if (Set(ref _hasSecondaryFindings, value))
            {
                Raise(nameof(ShowHealthySecondary));
            }
        }
    }

    public bool ShowHealthySecondary => !HasSecondaryFindings;

    public string SessionDurationText
    {
        get => _sessionDurationText;
        private set => Set(ref _sessionDurationText, value);
    }

    public string SamplesText
    {
        get => _samplesText;
        private set => Set(ref _samplesText, value);
    }

    public string AvgCpuText
    {
        get => _avgCpuText;
        private set => Set(ref _avgCpuText, value);
    }

    public string AvgGpuText
    {
        get => _avgGpuText;
        private set => Set(ref _avgGpuText, value);
    }

    public string AvgRamText
    {
        get => _avgRamText;
        private set => Set(ref _avgRamText, value);
    }

    public string PeakCpuText
    {
        get => _peakCpuText;
        private set => Set(ref _peakCpuText, value);
    }

    public string PeakGpuText
    {
        get => _peakGpuText;
        private set => Set(ref _peakGpuText, value);
    }

    public string PeakRamText
    {
        get => _peakRamText;
        private set => Set(ref _peakRamText, value);
    }

    public string PeakVramText
    {
        get => _peakVramText;
        private set => Set(ref _peakVramText, value);
    }

    public string ReportPathText
    {
        get => _reportPathText;
        private set => Set(ref _reportPathText, value);
    }

    public string LastReportPath
    {
        get => _lastReportPath;
        private set => Set(ref _lastReportPath, value);
    }

    public bool HasReport
    {
        get => _hasReport;
        private set
        {
            if (Set(ref _hasReport, value))
            {
                _openLastReportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LastError
    {
        get => _lastError;
        private set => Set(ref _lastError, value);
    }

    public string HardwareCpuName
    {
        get => _hardwareCpuName;
        private set => Set(ref _hardwareCpuName, value);
    }

    public string HardwareCpuDetail
    {
        get => _hardwareCpuDetail;
        private set => Set(ref _hardwareCpuDetail, value);
    }

    public string HardwareGpuName
    {
        get => _hardwareGpuName;
        private set => Set(ref _hardwareGpuName, value);
    }

    public string HardwareGpuDetail
    {
        get => _hardwareGpuDetail;
        private set => Set(ref _hardwareGpuDetail, value);
    }

    public string HardwareRam
    {
        get => _hardwareRam;
        private set => Set(ref _hardwareRam, value);
    }

    public string HardwareOs
    {
        get => _hardwareOs;
        private set => Set(ref _hardwareOs, value);
    }

    public string HardwarePrivileges
    {
        get => _hardwarePrivileges;
        private set => Set(ref _hardwarePrivileges, value);
    }

    public string SessionStartedText
    {
        get => _sessionStartedText;
        private set => Set(ref _sessionStartedText, value);
    }

    public string VerdictKind
    {
        get => _verdictKind;
        private set => Set(ref _verdictKind, value);
    }

    private void OnUpdated(object? sender, MonitoringUpdate update)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => ApplyUpdate(update)));
        }
        catch (TaskCanceledException)
        {
            // The window is closing.
        }
        catch (InvalidOperationException)
        {
            // The dispatcher has already shut down.
        }
    }

    private void OnError(object? sender, Exception exception)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    LastError = exception.Message;
                    StatusText = "СБОЙ ОПРОСА";
                    StatusBrush = Visuals.Brush(Visuals.Red);
                }));
        }
        catch (TaskCanceledException)
        {
            // The window is closing.
        }
        catch (InvalidOperationException)
        {
            // The dispatcher has already shut down.
        }
    }

    private void ApplyUpdate(MonitoringUpdate update)
    {
        if (_disposed)
        {
            return;
        }

        var first = _lastUpdate is null;
        _lastUpdate = update;

        UpdateHero(update.Diagnosis);
        UpdateMetrics(update);
        UpdateFindings(update.Diagnosis);
        UpdateProcesses(update.Snapshot, update.Diagnosis);
        UpdateSession(update.Stats, update.Snapshot.Timestamp);
        UpdateStatus(update.Snapshot.Timestamp);

        if (first)
        {
            _saveReportCommand.RaiseCanExecuteChanged();
        }
    }

    private void UpdateHero(DiagnosisResult diagnosis)
    {
        var primary = diagnosis.Primary;
        var severity = primary.Severity;

        VerdictTitle = primary.Title;
        VerdictSummary = primary.Summary;
        VerdictKind = primary.Kind.ToString();

        SeverityBrush = Visuals.SeverityBrush(severity);
        SeverityTintBrush = Visuals.SeverityTint(severity);
        HeroBrush = Visuals.HeroGradient(severity);

        SeverityText = primary.Kind switch
        {
            FindingKind.Calibrating => "СБОР ДАННЫХ",
            FindingKind.NoIssue => "ВСЁ В НОРМЕ",
            _ => primary.SeverityLabel.ToUpperInvariant()
        };

        ConfidenceText = diagnosis.IsCalibrating
            ? $"Наблюдение {Math.Min(diagnosis.SamplesObserved, WarmupSamples)} из {WarmupSamples}"
            : $"Уверенность: {ConfidenceWord(primary.Confidence)} · {diagnosis.SamplesObserved} с наблюдения";

        _evidence.Clear();
        foreach (var item in primary.Evidence.Take(4))
        {
            _evidence.Add(new EvidenceChipViewModel
            {
                Label = item.Label,
                Value = item.Value,
                Detail = item.Detail
            });
        }

        _primaryActions.Clear();
        var number = 1;
        foreach (var action in primary.Actions.Take(3))
        {
            _primaryActions.Add(new ActionItemViewModel { Number = number++, Text = action });
        }

        if (diagnosis.Target is { } target && primary.Kind != FindingKind.NoIssue)
        {
            HasTarget = true;
            HealthyTitle = "Ресурсы в норме";
            HealthyText = "Если лаги остались, сохраните отчёт в момент проблемы.";
            TargetName = target.DisplayName;
            TargetPidText = $"PID {target.Pid} · потоков: {target.ThreadCount}";
            TargetInitials = Visuals.Initials(target.Name);
            TargetBrush = Visuals.ProcessBrush(target.Name);
            TargetThreadValue = target.MaxThreadPercent >= 0.5
                ? Visuals.Pct(target.MaxThreadPercent)
                : "—";
            TargetThreadPercent = target.MaxThreadPercent;
            TargetThreadLabel = target.MaxThreadPercent >= 0.5
                ? "самый горячий поток"
                : "потоков не видно";
            TargetNote = target.MaxThreadPercent >= 82
                ? "Один поток близок к пределу. Остальные ядра при этом могут простаивать."
                : target.HottestThreadShare >= 0.55
                    ? "Главный поток даёт больше половины нагрузки процесса."
                    : "Нагрузка процесса распределена между потоками.";
            TargetStats =
                $"CPU {Visuals.Pct(target.CpuPercent)} · GPU {Visuals.Pct(target.GpuPercent)} · " +
                $"RAM {Visuals.FormatMb(target.WorkingSetMb)}";
        }
        else
        {
            HasTarget = false;
            if (diagnosis.IsCalibrating)
            {
                HealthyTitle = "Наблюдаю";
                HealthyText = "Собираю данные о потоках, памяти и диске. Вердикт появится через несколько секунд.";
            }
            else
            {
                HealthyTitle = "Ресурсы в норме";
                HealthyText = "Если лаги остались, сохраните отчёт в момент проблемы.";
            }
        }
    }

    private void UpdateMetrics(MonitoringUpdate update)
    {
        var s = update.Snapshot;
        var history = update.History;

        var cpuStatus = Status(s.Cpu.TotalPercent, 75, 90);
        var cpuGhz = s.Cpu.EffectiveFrequencyMhz > 0
            ? s.Cpu.EffectiveFrequencyMhz / 1000.0
            : s.Cpu.BaseFrequencyMhz / 1000.0;
        MetricCards[0].Update(
            Visuals.Pct(s.Cpu.TotalPercent),
            $"{s.Info.LogicalCores} потоков · {cpuGhz:0.0} ГГц",
            s.Cpu.TotalPercent,
            cpuStatus.Text,
            cpuStatus.Brush,
            history.Select(h => h.CpuPercent).ToArray());

        if (s.Gpu.Available)
        {
            var gpuStatus = Status(s.Gpu.LoadPercent, 85, 97);
            MetricCards[1].Update(
                Visuals.Pct(s.Gpu.LoadPercent),
                s.Gpu.AdapterName,
                s.Gpu.LoadPercent,
                gpuStatus.Text,
                gpuStatus.Brush,
                history.Select(h => h.GpuPercent).ToArray());
        }
        else
        {
            MetricCards[1].Update("н/д", "счётчики GPU недоступны", 0, "нет данных",
                Visuals.Brush("#5D6E82"), history.Select(h => h.GpuPercent).ToArray());
        }

        var ramStatus = Status(s.Memory.UsedPercent, 82, 92);
        MetricCards[2].Update(
            Visuals.Pct(s.Memory.UsedPercent),
            $"{Visuals.FormatGb(s.Memory.AvailableMb)} свободно",
            s.Memory.UsedPercent,
            ramStatus.Text,
            ramStatus.Brush,
            history.Select(h => h.RamPercent).ToArray());

        if (s.Gpu.IsIntegrated)
        {
            MetricCards[3].Update(
                "iGPU",
                "использует общую RAM",
                0,
                "интегрированная",
                Visuals.Brush(Visuals.Cyan),
                history.Select(h => h.VramPercent).ToArray());
        }
        else if (s.Gpu.VramUsagePercent is { } vram)
        {
            var vramStatus = Status(vram, 88, 95);
            MetricCards[3].Update(
                Visuals.Pct(vram),
                $"{Visuals.FormatGb(s.Gpu.VramUsedMb ?? 0)} / {Visuals.FormatGb(s.Gpu.VramTotalMb ?? 0)}",
                vram,
                vramStatus.Text,
                vramStatus.Brush,
                history.Select(h => h.VramPercent).ToArray());
        }
        else
        {
            MetricCards[3].Update("н/д", "счётчик VRAM недоступен", 0, "нет данных",
                Visuals.Brush("#5D6E82"), history.Select(h => h.VramPercent).ToArray());
        }

        var diskStatus = Status(s.Disk.ActivePercent, 80, 92);
        MetricCards[4].Update(
            Visuals.Pct(s.Disk.ActivePercent),
            $"Очередь {s.Disk.QueueLength:0.0} · {s.Disk.LatencyMs:0} мс",
            s.Disk.ActivePercent,
            diskStatus.Text,
            diskStatus.Brush,
            history.Select(h => h.DiskPercent).ToArray());
    }

    private void UpdateFindings(DiagnosisResult diagnosis)
    {
        _secondaryFindings.Clear();
        foreach (var finding in diagnosis.Secondary)
        {
            _secondaryFindings.Add(new FindingRowViewModel
            {
                Title = finding.Title,
                Summary = finding.Summary,
                Badge = finding.SeverityLabel,
                Accent = Visuals.SeverityBrush(finding.Severity),
                Tint = Visuals.SeverityTint(finding.Severity),
                Confidence = finding.Confidence
            });
        }

        HasSecondaryFindings = _secondaryFindings.Count > 0;
        SecondaryCountText = _secondaryFindings.Count > 0
            ? $"Ещё {_secondaryFindings.Count}"
            : "чисто";
    }

    private void UpdateProcesses(SystemSnapshot snapshot, DiagnosisResult diagnosis)
    {
        var targetPid = diagnosis.Target?.Pid;
        var incoming = new HashSet<int>(snapshot.Processes.Select(p => p.Pid));

        foreach (var dead in _rowsByPid.Keys.Where(pid => !incoming.Contains(pid)).ToArray())
        {
            if (_rowsByPid.Remove(dead, out var row))
            {
                _processes.Remove(row);
            }
        }

        foreach (var process in snapshot.Processes)
        {
            if (!_rowsByPid.TryGetValue(process.Pid, out var row))
            {
                row = new ProcessRowViewModel(process.Pid);
                _rowsByPid[process.Pid] = row;
                _processes.Add(row);
            }

            row.Update(process, process.Pid == targetPid);
        }

        _topProcesses.Clear();
        foreach (var process in snapshot.Processes
                     .OrderByDescending(p => p.ActivityScore)
                     .Take(4))
        {
            if (_rowsByPid.TryGetValue(process.Pid, out var row))
            {
                _topProcesses.Add(row);
            }
        }

        ProcessCountText = $"{_processes.Count} процессов";
        UpdateVisibleCount();
    }

    private void UpdateSession(SessionStats stats, DateTimeOffset timestamp)
    {
        SessionDurationText = FormatDuration(stats.Duration);
        SamplesText = stats.Samples.ToString();
        AvgCpuText = Visuals.Pct(stats.AvgCpuPercent);
        AvgGpuText = Visuals.Pct(stats.AvgGpuPercent);
        AvgRamText = Visuals.Pct(stats.AvgRamPercent);
        PeakCpuText = Visuals.Pct(stats.PeakCpuPercent);
        PeakGpuText = Visuals.Pct(stats.PeakGpuPercent);
        PeakRamText = Visuals.Pct(stats.PeakRamPercent);
        PeakVramText = Visuals.Pct(stats.PeakVramPercent);
        SessionStartedText = $"Сессия начата в {stats.StartedAt:HH:mm}";
    }

    private void UpdateStatus(DateTimeOffset? timestamp = null)
    {
        if (_service.IsPaused)
        {
            StatusText = "ПАУЗА";
            StatusBrush = Visuals.Brush(Visuals.Amber);
            PauseButtonText = "Продолжить";
            PauseGlyph = "▶";
        }
        else
        {
            StatusText = "LIVE · 1 Гц";
            StatusBrush = Visuals.Brush(Visuals.Green);
            PauseButtonText = "Пауза";
            PauseGlyph = "II";
        }

        UpdatedText = timestamp is null
            ? "ожидание данных"
            : $"обновлено {timestamp.Value:HH:mm:ss}";
    }

    private void TogglePause()
    {
        if (_service.IsPaused)
        {
            _service.Resume();
            IsPaused = false;
        }
        else
        {
            _service.Pause();
            IsPaused = true;
        }

        UpdateStatus(_lastUpdate?.Snapshot.Timestamp);
    }

    private void FillHardware(SystemInfo info)
    {
        HardwareCpuName = info.CpuName;
        HardwareCpuDetail =
            $"{info.PhysicalCores} ядер / {info.LogicalCores} потоков · {info.BaseFrequencyMhz / 1000:0.0} ГГц";
        HardwareGpuName = info.GpuName;
        HardwareGpuDetail = info.GpuMemoryMb > 0
            ? $"{Visuals.FormatGb(info.GpuMemoryMb)} видеопамяти"
            : "объём памяти неизвестен";
        HardwareRam = Visuals.FormatGb(info.RamTotalMb);
        HardwareOs = info.OsName;
        HardwarePrivileges = info.IsElevated ? "администратор" : "обычный пользователь";
    }

    private string ReportsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LoadLens");

    private void SaveReport()
    {
        if (_lastUpdate is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(ReportsFolder);
            var dialog = new SaveFileDialog
            {
                Title = "Сохранить отчёт LoadLens",
                Filter = "HTML-отчёт (*.html)|*.html",
                DefaultExt = ".html",
                AddExtension = true,
                OverwritePrompt = true,
                InitialDirectory = ReportsFolder,
                FileName = $"LoadLens_{DateTime.Now:yyyy-MM-dd_HH-mm}.html"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var html = HtmlReportBuilder.Build(_lastUpdate);
            File.WriteAllText(dialog.FileName, html, new UTF8Encoding(false));

            LastReportPath = dialog.FileName;
            HasReport = true;
            ReportPathText = $"Последний отчёт: {Path.GetFileName(dialog.FileName)}";
            LastError = string.Empty;
            OpenPath(dialog.FileName);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private void OpenLastReport()
    {
        if (!HasReport || !File.Exists(LastReportPath))
        {
            OpenReportsFolder();
            return;
        }

        OpenPath(LastReportPath);
    }
    private void OpenReportsFolder()
    {
        try
        {
            Directory.CreateDirectory(ReportsFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = ReportsFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private bool FilterProcess(object item)
    {
        if (item is not ProcessRowViewModel row)
        {
            return false;
        }

        return row.Matches(SearchText);
    }

    private void UpdateVisibleCount()
    {
        var visible = ProcessView.Cast<object>().Count();
        VisibleProcessCountText = $"{visible} из {_processes.Count}";
    }

    private static (string Text, Brush Brush) Status(double value, double high, double critical)
    {
        if (value >= critical)
        {
            return ("критично", Visuals.Brush(Visuals.Red));
        }

        if (value >= high)
        {
            return ("высоко", Visuals.Brush(Visuals.Orange));
        }

        return ("норма", Visuals.Brush(Visuals.Green));
    }

    private static string ConfidenceWord(double confidence) => confidence switch
    {
        >= 0.85 => "высокая",
        >= 0.7 => "средняя",
        _ => "осторожная"
    };

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} ч {duration.Minutes} мин";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes} мин {duration.Seconds} с";
        }

        return $"{Math.Max(0, duration.Seconds)} с";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _service.Updated -= OnUpdated;
        _service.Error -= OnError;
        _service.Dispose();
    }
}
