namespace LoadLens.Core.Models;

/// <summary>CPU metrics for one sampling interval.</summary>
public sealed record CpuMetrics(
    double TotalPercent,
    IReadOnlyList<double> PerCorePercent,
    double MaxCorePercent,
    double DpcPercent,
    double InterruptPercent,
    double ProcessorQueueLength,
    double PerformancePercent,
    double BaseFrequencyMhz,
    double EffectiveFrequencyMhz)
{
    public static CpuMetrics Empty(double baseFrequencyMhz = 0) => new(
        0,
        Array.Empty<double>(),
        0,
        0,
        0,
        0,
        100,
        baseFrequencyMhz,
        baseFrequencyMhz);
}

/// <summary>GPU metrics for the adapter that is doing the most work.</summary>
public sealed record GpuMetrics(
    string AdapterName,
    bool Available,
    double LoadPercent,
    double? VramUsedMb,
    double? VramTotalMb,
    double? VramSharedMb,
    double? VramUsagePercent,
    double? SharedMemoryLimitMb,
    bool IsIntegrated = false)
{
    public static GpuMetrics Unavailable(string adapterName = "Видеоадаптер не найден") =>
        new(adapterName, false, 0, null, null, null, null, null);
}

/// <summary>Physical memory and commit charge.</summary>
public sealed record MemoryMetrics(
    double TotalMb,
    double AvailableMb,
    double UsedPercent,
    double CommitUsedMb,
    double CommitLimitMb,
    double CommitPercent,
    double PagesPerSecond,
    double PageFaultsPerSecond,
    double PagesInputPerSecond,
    double PagingFilePercent);

/// <summary>Aggregated physical-disk activity.</summary>
public sealed record DiskMetrics(
    double ActivePercent,
    double QueueLength,
    double LatencyMs,
    double ReadMbps,
    double WriteMbps)
{
    public static readonly DiskMetrics Empty = new(0, 0, 0, 0, 0);
}

/// <summary>One process as seen during a single sample.</summary>
public sealed record ProcessMetrics(
    int Pid,
    string Name,
    double CpuPercent,
    double MaxThreadPercent,
    double HottestThreadShare,
    int ThreadCount,
    double WorkingSetMb,
    double PrivateMb,
    double GpuPercent,
    double VramMb,
    double SharedVramMb,
    bool HasWindow,
    double ActivityScore)
{
    public string DisplayName => Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        ? Name
        : Name + ".exe";
}

/// <summary>Static information about the machine.</summary>
public sealed record SystemInfo(
    string CpuName,
    int PhysicalCores,
    int LogicalCores,
    double BaseFrequencyMhz,
    string GpuName,
    double GpuMemoryMb,
    double RamTotalMb,
    string OsName,
    string MachineName,
    string AppVersion,
    bool IsElevated);

/// <summary>A compact point used for the live sparklines.</summary>
public sealed record HistoryPoint(
    DateTimeOffset Timestamp,
    double CpuPercent,
    double GpuPercent,
    double RamPercent,
    double VramPercent,
    double DiskPercent);

/// <summary>Aggregate values for the current monitoring session.</summary>
public sealed record SessionStats(
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int Samples,
    double AvgCpuPercent,
    double AvgGpuPercent,
    double AvgRamPercent,
    double PeakCpuPercent,
    double PeakGpuPercent,
    double PeakRamPercent,
    double PeakVramPercent);

/// <summary>Everything the UI needs after one sampling tick.</summary>
public sealed record MonitoringUpdate(
    SystemSnapshot Snapshot,
    DiagnosisResult Diagnosis,
    IReadOnlyList<HistoryPoint> History,
    SessionStats Stats);

/// <summary>Complete immutable snapshot of the machine.</summary>
public sealed record SystemSnapshot(
    DateTimeOffset Timestamp,
    double ElapsedSeconds,
    int SampleNumber,
    CpuMetrics Cpu,
    GpuMetrics Gpu,
    MemoryMetrics Memory,
    DiskMetrics Disk,
    IReadOnlyList<ProcessMetrics> Processes,
    SystemInfo Info);
