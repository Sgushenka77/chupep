using System.Diagnostics;
using LoadLens.Core.Models;

namespace LoadLens.Core.Monitoring;

/// <summary>
/// Orchestrates all meters and produces one immutable snapshot per tick.
/// </summary>
public sealed class SystemSampler : IDisposable
{
    private readonly PdhQuery _pdh;
    private readonly GpuMeter _gpuMeter;
    private readonly CpuMeter _cpuMeter;
    private readonly ProcessMeter _processMeter;
    private readonly SystemInfo _info;
    private readonly Stopwatch _clock;

    private double _lastElapsed;
    private int _sampleNumber;
    private bool _disposed;

    public SystemSampler(string appVersion = "1.0.0")
    {
        _pdh = new PdhQuery();
        _gpuMeter = new GpuMeter();
        _info = SystemInfoProvider.Read(_gpuMeter.Adapters, appVersion);
        _cpuMeter = new CpuMeter(_info.LogicalCores, _info.BaseFrequencyMhz);
        _processMeter = new ProcessMeter();
        _clock = Stopwatch.StartNew();
    }

    public SystemInfo Info => _info;

    public SystemSnapshot Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var nowElapsed = _clock.Elapsed.TotalSeconds;
        var elapsed = _sampleNumber == 0 ? 0.2 : Math.Max(0.05, nowElapsed - _lastElapsed);
        _lastElapsed = nowElapsed;

        _pdh.Collect();

        var cpu = _cpuMeter.Read(_pdh) with
        {
            ProcessorQueueLength = Math.Max(0, _pdh.GetValue(PdhQuery.ProcessorQueue) ?? 0)
        };
        var memory = MemoryMeter.Read(_pdh);
        var disk = DiskMeter.Read(_pdh);
        var gpuReading = _gpuMeter.Read(_pdh);
        var processes = _processMeter.Read(elapsed, _info.LogicalCores, gpuReading);

        _sampleNumber++;

        return new SystemSnapshot(
            DateTimeOffset.Now,
            elapsed,
            _sampleNumber,
            cpu,
            gpuReading.Metrics,
            memory,
            disk,
            processes,
            _info);
    }

    /// <summary>Drops rate baselines (used after a pause).</summary>
    public void ResetBaselines()
    {
        _cpuMeter.Reset();
        _processMeter.Reset();
        _lastElapsed = _clock.Elapsed.TotalSeconds;
        _sampleNumber = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pdh.Dispose();
    }
}
