using LoadLens.Core.Models;
using LoadLens.Core.Monitoring;

namespace LoadLens.Core.Diagnostics;

/// <summary>
/// Owns the sampling loop, the rolling history and the diagnostic engine.
/// </summary>
public sealed class MonitoringService : IDisposable
{
    private const int HistoryLength = 60;

    private readonly SystemSampler _sampler;
    private readonly DiagnosisEngine _diagnosis;
    private readonly List<HistoryPoint> _history = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly object _gate = new();

    private Task? _loop;
    private volatile bool _paused;
    private bool _resetOnNextTick;
    private bool _disposed;

    private DateTimeOffset _startedAt;
    private int _samples;
    private double _sumCpu;
    private double _sumGpu;
    private double _sumRam;
    private double _peakCpu;
    private double _peakGpu;
    private double _peakRam;
    private double _peakVram;

    public MonitoringService(string appVersion = "1.0.0")
    {
        _sampler = new SystemSampler(appVersion);
        _diagnosis = new DiagnosisEngine(_sampler.Info.LogicalCores);
        _startedAt = DateTimeOffset.Now;
    }

    public event EventHandler<MonitoringUpdate>? Updated;

    public event EventHandler<Exception>? Error;

    public SystemInfo Info => _sampler.Info;

    public bool IsPaused => _paused;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _loop ??= Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Pause() => _paused = true;

    public void Resume()
    {
        if (!_paused)
        {
            return;
        }

        _resetOnNextTick = true;
        _paused = false;
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!_paused)
            {
                if (_resetOnNextTick)
                {
                    ResetSession();
                    _resetOnNextTick = false;
                }

                try
                {
                    var snapshot = await Task.Run(_sampler.Sample, token).ConfigureAwait(false);
                    var diagnosis = _diagnosis.Evaluate(snapshot);
                    var update = BuildUpdate(snapshot, diagnosis);
                    Updated?.Invoke(this, update);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Error?.Invoke(this, ex);
                }
            }

            try
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private MonitoringUpdate BuildUpdate(SystemSnapshot snapshot, DiagnosisResult diagnosis)
    {
        lock (_gate)
        {
            if (_samples == 0)
            {
                _startedAt = snapshot.Timestamp;
            }

            _samples++;
            _sumCpu += snapshot.Cpu.TotalPercent;
            _sumGpu += snapshot.Gpu.Available ? snapshot.Gpu.LoadPercent : 0;
            _sumRam += snapshot.Memory.UsedPercent;
            _peakCpu = Math.Max(_peakCpu, snapshot.Cpu.TotalPercent);
            _peakGpu = Math.Max(_peakGpu, snapshot.Gpu.Available ? snapshot.Gpu.LoadPercent : 0);
            _peakRam = Math.Max(_peakRam, snapshot.Memory.UsedPercent);
            _peakVram = Math.Max(_peakVram, snapshot.Gpu.VramUsagePercent ?? 0);

            _history.Add(new HistoryPoint(
                snapshot.Timestamp,
                snapshot.Cpu.TotalPercent,
                snapshot.Gpu.Available ? snapshot.Gpu.LoadPercent : 0,
                snapshot.Memory.UsedPercent,
                snapshot.Gpu.VramUsagePercent ?? 0,
                snapshot.Disk.ActivePercent));

            if (_history.Count > HistoryLength)
            {
                _history.RemoveRange(0, _history.Count - HistoryLength);
            }

            var stats = new SessionStats(
                _startedAt,
                snapshot.Timestamp - _startedAt,
                _samples,
                _sumCpu / _samples,
                _sumGpu / _samples,
                _sumRam / _samples,
                _peakCpu,
                _peakGpu,
                _peakRam,
                _peakVram);

            return new MonitoringUpdate(snapshot, diagnosis, _history.ToArray(), stats);
        }
    }

    private void ResetSession()
    {
        lock (_gate)
        {
            _sampler.ResetBaselines();
            _diagnosis.Reset();
            _history.Clear();
            _samples = 0;
            _sumCpu = 0;
            _sumGpu = 0;
            _sumRam = 0;
            _peakCpu = 0;
            _peakGpu = 0;
            _peakRam = 0;
            _peakVram = 0;
            _startedAt = DateTimeOffset.Now;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Cancellation is expected.
        }

        _cts.Dispose();
        _sampler.Dispose();
    }
}
