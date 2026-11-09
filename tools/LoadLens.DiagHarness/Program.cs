using System.Globalization;
using System.Text;
using LoadLens.Core.Diagnostics;
using LoadLens.Core.Models;
using LoadLens.Core.Monitoring;
using LoadLens.Core.Reports;

var culture = CultureInfo.GetCultureInfo("ru-RU");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.CurrentCulture = culture;

if (args.Length > 0 && args[0].Equals("report", StringComparison.OrdinalIgnoreCase))
{
    var output = args.Length > 1
        ? Path.GetFullPath(args[1])
        : Path.GetFullPath("LoadLens-report.html");
    var update = BuildReportUpdate();
    File.WriteAllText(output, HtmlReportBuilder.Build(update), new UTF8Encoding(false));
    Console.WriteLine(output);
    return 0;
}

if (args.Length > 0 && args[0].Equals("probe", StringComparison.OrdinalIgnoreCase))
{
    var seconds = args.Length > 1 && int.TryParse(args[1], out var parsed) ? Math.Clamp(parsed, 2, 60) : 6;
    return RunProbe(seconds);
}

return RunDiagnosticTests();

static int RunProbe(int seconds)
{
    using var sampler = new SystemSampler("1.0.0-probe");
    var engine = new DiagnosisEngine(sampler.Info.LogicalCores);
    var info = sampler.Info;
    Console.WriteLine("=== LoadLens probe ===");
    Console.WriteLine($"CPU: {info.CpuName} ({info.PhysicalCores}C/{info.LogicalCores}T, {info.BaseFrequencyMhz:0} MHz)");
    Console.WriteLine($"GPU: {info.GpuName} ({info.GpuMemoryMb:0} MB)");
    Console.WriteLine($"RAM: {info.RamTotalMb:0} MB | {info.OsName}");
    Console.WriteLine();

    for (var i = 0; i < seconds; i++)
    {
        var sample = sampler.Sample();
        var diagnosis = engine.Evaluate(sample);
        Console.WriteLine(
            $"[{i + 1}] CPU {sample.Cpu.TotalPercent:0.0}% (max core {sample.Cpu.MaxCorePercent:0.0}%, " +
            $"{sample.Cpu.EffectiveFrequencyMhz:0} MHz, queue {sample.Cpu.ProcessorQueueLength:0.0}, " +
            $"DPC {sample.Cpu.DpcPercent:0.0}%) cores=[{string.Join(", ", sample.Cpu.PerCorePercent.Select(v => v.ToString("0.0")))}]");
        Console.WriteLine(
            $"    GPU {sample.Gpu.LoadPercent:0.0}% | VRAM " +
            $"{sample.Gpu.VramUsedMb:0}/{sample.Gpu.VramTotalMb:0} MB " +
            $"({sample.Gpu.VramUsagePercent:0.0}%), shared {sample.Gpu.VramSharedMb:0} MB, " +
            $"available={sample.Gpu.Available}, {sample.Gpu.AdapterName}");
        Console.WriteLine(
            $"    RAM {sample.Memory.UsedPercent:0.0}% ({sample.Memory.AvailableMb:0} MB free), " +
            $"commit {sample.Memory.CommitPercent:0.0}%, pages input {sample.Memory.PagesInputPerSecond:0}/s | " +
            $"disk {sample.Disk.ActivePercent:0}% q={sample.Disk.QueueLength:0.0} {sample.Disk.LatencyMs:0} ms");
        Console.WriteLine(
            $"    VERDICT [{diagnosis.Primary.Severity}] {diagnosis.Primary.Title} " +
            $"(target: {diagnosis.Target?.DisplayName ?? "нет"}, samples: {diagnosis.SamplesObserved})");
        Console.WriteLine("    Top processes:");
        foreach (var p in sample.Processes.Take(6))
        {
            Console.WriteLine(
                $"      {p.DisplayName,-28} PID {p.Pid,6} CPU {p.CpuPercent,5:0.0}% " +
                $"1thr {p.MaxThreadPercent,5:0.0}% share {p.HottestThreadShare,4:0.00} " +
                $"GPU {p.GpuPercent,4:0}% VRAM {p.VramMb,6:0} MB RAM {p.WorkingSetMb,7:0} MB win={p.HasWindow}");
        }

        Console.WriteLine();
        Thread.Sleep(1000);
    }

    return 0;
}

static int RunDiagnosticTests()
{
    var failures = 0;
    var total = 0;

    void Case(string name, FindingKind expected, Func<DateTimeOffset, int, SystemSnapshot> factory, int samples = 5)
    {
        total++;
        var engine = new DiagnosisEngine(8);
        DiagnosisResult result = null!;
        var start = DateTimeOffset.Now;

        for (var i = 0; i < samples; i++)
        {
            result = engine.Evaluate(factory(start.AddSeconds(i), i + 1));
        }

        if (result.Primary.Kind != expected)
        {
            failures++;
            Console.WriteLine($"FAIL  {name}: expected {expected}, got {result.Primary.Kind} — {result.Primary.Title}");
            Console.WriteLine($"      evidence: {string.Join(" | ", result.Primary.Evidence.Select(e => $"{e.Label}={e.Value}"))}");
        }
        else
        {
            Console.WriteLine($"PASS  {name}: {result.Primary.Kind} / {result.Primary.Severity} — {result.Primary.Title}");
        }
    }

    Case("single-thread bottleneck", FindingKind.SingleThread, (ts, n) => Snapshot(ts, n, cfg =>
    {
        cfg.CpuTotal = 30;
        cfg.CpuMaxCore = 98;
        cfg.GpuLoad = 50;
        cfg.ProcessCpu = 12.5;
        cfg.MaxThread = 98;
        cfg.ThreadShare = 0.94;
        cfg.ProcessGpu = 55;
    }));

    Case("VRAM pressure", FindingKind.VramPressure, (ts, n) => Snapshot(ts, n, cfg =>
    {
        cfg.CpuTotal = 42;
        cfg.CpuMaxCore = 55;
        cfg.GpuLoad = 76;
        cfg.VramUsed = 3980;
        cfg.VramTotal = 4096;
        cfg.VramShared = 1200;
        cfg.ProcessCpu = 12;
        cfg.MaxThread = 58;
        cfg.ProcessGpu = 70;
        cfg.ProcessVram = 3600;
    }));

    Case("RAM pressure", FindingKind.RamPressure, (ts, n) => Snapshot(ts, n, cfg =>
    {
        cfg.CpuTotal = 36;
        cfg.AvailableRam = 480;
        cfg.CommitPercent = 94;
        cfg.PagesInput = 320;
        cfg.GpuLoad = 42;
    }));

    Case("GPU bound", FindingKind.GpuBound, (ts, n) => Snapshot(ts, n, cfg =>
    {
        cfg.CpuTotal = 38;
        cfg.GpuLoad = 99;
        cfg.VramUsed = 2800;
        cfg.ProcessCpu = 9;
        cfg.MaxThread = 45;
        cfg.ProcessGpu = 96;
    }));

    Case("thermal throttling", FindingKind.ThermalThrottle, (ts, n) => Snapshot(ts, n, cfg =>
    {
        cfg.CpuTotal = 79;
        cfg.CpuPerformance = 62;
        cfg.GpuLoad = 44;
        cfg.ProcessCpu = 55;
        cfg.MaxThread = 70;
    }));

    Case("CPU saturated", FindingKind.CpuSaturated, (ts, n) => Snapshot(ts, n, cfg =>
    {
        cfg.CpuTotal = 97;
        cfg.CpuQueue = 10;
        cfg.GpuLoad = 38;
        cfg.ProcessCpu = 72;
        cfg.MaxThread = 80;
        cfg.ThreadShare = 0.5;
    }));

    Case("healthy", FindingKind.NoIssue, (ts, n) => Snapshot(ts, n, cfg =>
    {
        cfg.CpuTotal = 24;
        cfg.GpuLoad = 56;
        cfg.VramUsed = 1900;
        cfg.ProcessCpu = 8;
        cfg.MaxThread = 42;
        cfg.ProcessGpu = 44;
    }));

    Case("warmup", FindingKind.Calibrating, (ts, n) => Snapshot(ts, n, _ => { }), samples: 1);

    var reportUpdate = BuildReportUpdate();
    var report = HtmlReportBuilder.Build(reportUpdate);
    total++;
    if (report.Length < 5000 || !report.Contains("LoadLens", StringComparison.Ordinal))
    {
        failures++;
        Console.WriteLine("FAIL  HTML report");
    }
    else
    {
        Console.WriteLine($"PASS  HTML report: {report.Length:N0} chars");
    }

    Console.WriteLine();
    Console.WriteLine(failures == 0
        ? $"All {total} checks passed."
        : $"{failures} of {total} checks failed.");
    return failures == 0 ? 0 : 1;
}

static MonitoringUpdate BuildReportUpdate()
{
    var snapshot = Snapshot(DateTimeOffset.Now, 5, cfg =>
    {
        cfg.CpuTotal = 30;
        cfg.CpuMaxCore = 98;
        cfg.GpuLoad = 50;
        cfg.ProcessCpu = 12.5;
        cfg.MaxThread = 98;
        cfg.ThreadShare = 0.94;
        cfg.ProcessGpu = 55;
    });

    var engine = new DiagnosisEngine(8);
    DiagnosisResult diagnosis = null!;
    for (var i = 0; i < 5; i++)
    {
        diagnosis = engine.Evaluate(snapshot);
    }

    var history = Enumerable.Range(0, 20)
        .Select(i => new HistoryPoint(snapshot.Timestamp.AddSeconds(i - 20), 20 + i, 45, 50, 52, 8))
        .ToArray();
    var stats = new SessionStats(
        snapshot.Timestamp.AddSeconds(-20),
        TimeSpan.FromSeconds(20),
        20,
        30,
        50,
        52,
        98,
        70,
        60,
        80);

    return new MonitoringUpdate(snapshot, diagnosis, history, stats);
}

static SystemSnapshot Snapshot(
    DateTimeOffset timestamp,
    int sample,
    Action<Scenario> configure)
{
    var cfg = new Scenario();
    configure(cfg);

    var target = new ProcessMetrics(
        4242,
        "game",
        cfg.ProcessCpu,
        cfg.MaxThread,
        cfg.ThreadShare,
        24,
        6200,
        7100,
        cfg.ProcessGpu,
        cfg.ProcessVram,
        Math.Max(0, cfg.VramShared - 300),
        true,
        100);

    var background = new ProcessMetrics(
        100,
        "explorer",
        0.4,
        1,
        0.5,
        40,
        180,
        240,
        0,
        0,
        0,
        true,
        3);

    var chrome = new ProcessMetrics(
        2200,
        "chrome",
        5,
        18,
        0.4,
        38,
        2800,
        3200,
        cfg.GpuLoad > 80 ? 8 : 2,
        240,
        80,
        true,
        8);

    var info = new SystemInfo(
        "Intel Core i7-9700",
        8,
        8,
        3000,
        "Radeon RX 550",
        4096,
        32768,
        "Windows 11 · сборка 26200",
        "TEST-PC",
        "1.0.0-test",
        false);

    var cores = Enumerable.Repeat(cfg.CpuTotal, 8).ToArray();
    var commitLimit = 40000d;
    var commitUsed = commitLimit * cfg.CommitPercent / 100;
    var vramPercent = cfg.VramTotal > 0 ? Math.Clamp(cfg.VramUsed / cfg.VramTotal * 100, 0, 100) : 0;
    var effective = 3000 * cfg.CpuPerformance / 100;

    return new SystemSnapshot(
        timestamp,
        1,
        sample,
        new CpuMetrics(
            cfg.CpuTotal,
            cores,
            cfg.CpuMaxCore,
            2,
            1,
            cfg.CpuQueue,
            cfg.CpuPerformance,
            3000,
            effective),
        new GpuMetrics(
            "Radeon RX 550",
            true,
            cfg.GpuLoad,
            cfg.VramUsed,
            cfg.VramTotal,
            cfg.VramShared,
            vramPercent,
            16000),
        new MemoryMetrics(
            32768,
            cfg.AvailableRam,
            (32768 - cfg.AvailableRam) / 32768 * 100,
            commitUsed,
            commitLimit,
            cfg.CommitPercent,
            cfg.PagesInput * 1.2,
            2000,
            cfg.PagesInput,
            10),
        new DiskMetrics(cfg.DiskActive, cfg.DiskQueue, cfg.DiskLatency, 2, 1),
        new[] { target, chrome, background },
        info);
}

internal sealed class Scenario
{
    public double CpuTotal { get; set; } = 30;
    public double CpuMaxCore { get; set; } = 40;
    public double CpuQueue { get; set; }
    public double CpuPerformance { get; set; } = 100;
    public double GpuLoad { get; set; } = 40;
    public double VramUsed { get; set; } = 1800;
    public double VramTotal { get; set; } = 4096;
    public double VramShared { get; set; } = 120;
    public double AvailableRam { get; set; } = 14000;
    public double CommitPercent { get; set; } = 55;
    public double PagesInput { get; set; } = 10;
    public double DiskActive { get; set; } = 8;
    public double DiskQueue { get; set; } = 0.2;
    public double DiskLatency { get; set; } = 2;
    public double ProcessCpu { get; set; } = 8;
    public double MaxThread { get; set; } = 40;
    public double ThreadShare { get; set; } = 0.5;
    public double ProcessGpu { get; set; } = 35;
    public double ProcessVram { get; set; } = 1600;
}
