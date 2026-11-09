using System.Runtime.InteropServices;
using LoadLens.Core.Models;

namespace LoadLens.Core.Monitoring;

/// <summary>
/// Reads total and per-logical-core CPU time directly from
/// NtQuerySystemInformation(SystemProcessorPerformanceInformation).
/// </summary>
internal sealed class CpuMeter
{
    private const int SystemProcessorPerformanceInformation = 8;

    private readonly int _logicalCores;
    private readonly double _baseFrequencyMhz;
    private readonly int _structureSize;

    private long[] _prevIdle = Array.Empty<long>();
    private long[] _prevKernel = Array.Empty<long>();
    private long[] _prevUser = Array.Empty<long>();
    private long[] _prevDpc = Array.Empty<long>();
    private long[] _prevInterrupt = Array.Empty<long>();
    private bool _perCorePrimed;

    private long _prevTotalIdle;
    private long _prevTotalKernel;
    private long _prevTotalUser;
    private bool _fallbackPrimed;

    public CpuMeter(int logicalCores, double baseFrequencyMhz)
    {
        _logicalCores = Math.Max(1, logicalCores);
        _baseFrequencyMhz = baseFrequencyMhz;
        _structureSize = Marshal.SizeOf<NativeMethods.SystemProcessorPerformanceInformation>();
    }

    public void Reset()
    {
        _perCorePrimed = false;
        _fallbackPrimed = false;
        _prevIdle = Array.Empty<long>();
        _prevKernel = Array.Empty<long>();
        _prevUser = Array.Empty<long>();
        _prevDpc = Array.Empty<long>();
        _prevInterrupt = Array.Empty<long>();
    }

    public CpuMetrics Read(PdhQuery pdh)
    {
        var performance = pdh.GetValue(PdhQuery.ProcessorPerformance) ?? 100;
        performance = Math.Clamp(performance, 0, 250);

        var perCore = ReadPerCore();
        CpuMetrics metrics;
        if (perCore is not null)
        {
            metrics = perCore;
        }
        else
        {
            metrics = ReadFallback();
        }

        var effective = _baseFrequencyMhz * performance / 100.0;
        return metrics with
        {
            PerformancePercent = performance,
            BaseFrequencyMhz = _baseFrequencyMhz,
            EffectiveFrequencyMhz = effective
        };
    }

    private CpuMetrics? ReadPerCore()
    {
        var bufferSize = _structureSize * Math.Max(64, _logicalCores + 8);
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            var status = NativeMethods.NtQuerySystemInformation(
                SystemProcessorPerformanceInformation,
                buffer,
                bufferSize,
                out var returnedLength);

            if (status < 0 || returnedLength <= 0)
            {
                return null;
            }

            var count = returnedLength / _structureSize;
            if (count <= 0)
            {
                return null;
            }

            var idle = new long[count];
            var kernel = new long[count];
            var user = new long[count];
            var dpc = new long[count];
            var interrupt = new long[count];

            for (var i = 0; i < count; i++)
            {
                var ptr = IntPtr.Add(buffer, i * _structureSize);
                var info = Marshal.PtrToStructure<NativeMethods.SystemProcessorPerformanceInformation>(ptr);
                idle[i] = info.IdleTime;
                kernel[i] = info.KernelTime;
                user[i] = info.UserTime;
                dpc[i] = info.DpcTime;
                interrupt[i] = info.InterruptTime;
            }

            if (!_perCorePrimed || _prevIdle.Length != count)
            {
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _prevDpc = dpc;
                _prevInterrupt = interrupt;
                _perCorePrimed = true;
                return CpuMetrics.Empty(_baseFrequencyMhz);
            }

            var percentages = new double[count];
            double totalDelta = 0;
            double busyDelta = 0;
            double dpcDelta = 0;
            double interruptDelta = 0;

            for (var i = 0; i < count; i++)
            {
                var total = (kernel[i] - _prevKernel[i]) + (user[i] - _prevUser[i]);
                var idlePart = idle[i] - _prevIdle[i];
                var busy = Math.Max(0, total - idlePart);
                percentages[i] = total > 0 ? Math.Clamp((double)busy / total * 100.0, 0, 100) : 0;

                totalDelta += Math.Max(0, total);
                busyDelta += busy;
                dpcDelta += Math.Max(0, dpc[i] - _prevDpc[i]);
                interruptDelta += Math.Max(0, interrupt[i] - _prevInterrupt[i]);
            }

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _prevDpc = dpc;
            _prevInterrupt = interrupt;

            var totalPercent = totalDelta > 0 ? Math.Clamp(busyDelta / totalDelta * 100.0, 0, 100) : 0;
            var dpcPercent = totalDelta > 0 ? Math.Clamp(dpcDelta / totalDelta * 100.0, 0, 100) : 0;
            var interruptPercent = totalDelta > 0 ? Math.Clamp(interruptDelta / totalDelta * 100.0, 0, 100) : 0;

            return new CpuMetrics(
                totalPercent,
                percentages,
                percentages.DefaultIfEmpty(0).Max(),
                dpcPercent,
                interruptPercent,
                0,
                100,
                _baseFrequencyMhz,
                _baseFrequencyMhz);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private CpuMetrics ReadFallback()
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return CpuMetrics.Empty(_baseFrequencyMhz);
        }

        if (!_fallbackPrimed)
        {
            _prevTotalIdle = idle;
            _prevTotalKernel = kernel;
            _prevTotalUser = user;
            _fallbackPrimed = true;
            return CpuMetrics.Empty(_baseFrequencyMhz);
        }

        var total = (kernel - _prevTotalKernel) + (user - _prevTotalUser);
        var idleDelta = idle - _prevTotalIdle;
        var busy = Math.Max(0, total - idleDelta);
        var percent = total > 0 ? Math.Clamp(busy / total * 100.0, 0, 100) : 0;

        _prevTotalIdle = idle;
        _prevTotalKernel = kernel;
        _prevTotalUser = user;

        return new CpuMetrics(
            percent,
            Array.Empty<double>(),
            percent,
            0,
            0,
            0,
            100,
            _baseFrequencyMhz,
            _baseFrequencyMhz);
    }
}
