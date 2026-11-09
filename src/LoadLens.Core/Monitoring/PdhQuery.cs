using System.Runtime.InteropServices;

namespace LoadLens.Core.Monitoring;

/// <summary>
/// Thin wrapper around the native PDH API. English counter names are used on
/// purpose: they work on localized Windows installations as well.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    public const string GpuEngineUtilization = @"\GPU Engine(*)\Utilization Percentage";
    public const string GpuAdapterDedicated = @"\GPU Adapter Memory(*)\Dedicated Usage";
    public const string GpuAdapterShared = @"\GPU Adapter Memory(*)\Shared Usage";
    public const string GpuLocalAdapter = @"\GPU Local Adapter Memory(*)\Local Usage";
    public const string GpuProcessDedicated = @"\GPU Process Memory(*)\Dedicated Usage";
    public const string GpuProcessLocal = @"\GPU Process Memory(*)\Local Usage";
    public const string GpuProcessShared = @"\GPU Process Memory(*)\Shared Usage";

    public const string DiskTime = @"\PhysicalDisk(_Total)\% Disk Time";
    public const string DiskQueue = @"\PhysicalDisk(_Total)\Current Disk Queue Length";
    public const string DiskLatency = @"\PhysicalDisk(_Total)\Avg. Disk sec/Transfer";
    public const string DiskRead = @"\PhysicalDisk(_Total)\Disk Read Bytes/sec";
    public const string DiskWrite = @"\PhysicalDisk(_Total)\Disk Write Bytes/sec";

    public const string ProcessorQueue = @"\System\Processor Queue Length";
    public const string DpcTime = @"\Processor(_Total)\% DPC Time";
    public const string InterruptTime = @"\Processor(_Total)\% Interrupt Time";
    public const string ProcessorPerformance = @"\Processor Information(_Total)\% Processor Performance";

    public const string PagesPerSecond = @"\Memory\Pages/sec";
    public const string PageFaults = @"\Memory\Page Faults/sec";
    public const string PagesInput = @"\Memory\Pages Input/sec";
    public const string PagingFile = @"\Paging File(_Total)\% Usage";

    private const uint PdFmtDouble = 0x00000200;
    private const int PdhMoreData = unchecked((int)0x800007D2);
    private const int PdhStatusValidData = 0;
    private const int PdhStatusNewData = 1;

    private static readonly string[] AllCounters =
    {
        GpuEngineUtilization,
        GpuAdapterDedicated,
        GpuAdapterShared,
        GpuLocalAdapter,
        GpuProcessDedicated,
        GpuProcessLocal,
        GpuProcessShared,
        DiskTime,
        DiskQueue,
        DiskLatency,
        DiskRead,
        DiskWrite,
        ProcessorQueue,
        DpcTime,
        InterruptTime,
        ProcessorPerformance,
        PagesPerSecond,
        PageFaults,
        PagesInput,
        PagingFile
    };

    private readonly IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public PdhQuery()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0)
        {
            _query = IntPtr.Zero;
            return;
        }

        foreach (var path in AllCounters)
        {
            AddCounter(path);
        }

        // First collection only primes rate counters. The next one produces values.
        PdhCollectQueryData(_query);
    }

    public bool IsAvailable => !_disposed && _query != IntPtr.Zero && _counters.Count > 0;

    public bool HasCounter(string path) => _counters.ContainsKey(path);

    public bool Collect()
    {
        if (!IsAvailable)
        {
            return false;
        }

        return PdhCollectQueryData(_query) == 0;
    }

    public double? GetValue(string path)
    {
        if (_disposed || !_counters.TryGetValue(path, out var counter))
        {
            return null;
        }

        if (PdhGetFormattedCounterValue(counter, PdFmtDouble, out _, out var value) != 0)
        {
            return null;
        }

        if (value.CStatus is not (PdhStatusValidData or PdhStatusNewData))
        {
            return null;
        }

        return double.IsFinite(value.DoubleValue) ? value.DoubleValue : null;
    }

    public IReadOnlyList<CounterInstance> GetInstances(string path)
    {
        if (_disposed || !_counters.TryGetValue(path, out var counter))
        {
            return Array.Empty<CounterInstance>();
        }

        uint size = 0;
        var status = PdhGetFormattedCounterArrayW(counter, PdFmtDouble, ref size, out _, IntPtr.Zero);
        if (status != PdhMoreData || size == 0)
        {
            return Array.Empty<CounterInstance>();
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = PdhGetFormattedCounterArrayW(counter, PdFmtDouble, ref size, out var itemCount, buffer);
            if (status != 0 || itemCount == 0)
            {
                return Array.Empty<CounterInstance>();
            }

            var result = new List<CounterInstance>((int)itemCount);
            var itemSize = Marshal.SizeOf<PdhFmtCounterValueItem>();
            for (var i = 0; i < itemCount; i++)
            {
                var itemPtr = IntPtr.Add(buffer, i * itemSize);
                var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(itemPtr);
                if (item.Value.CStatus is not (PdhStatusValidData or PdhStatusNewData) ||
                    !double.IsFinite(item.Value.DoubleValue))
                {
                    continue;
                }

                var name = Marshal.PtrToStringUni(item.Name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(new CounterInstance(name, item.Value.DoubleValue));
                }
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void AddCounter(string path)
    {
        if (PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var counter) == 0)
        {
            _counters[path] = counter;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
        }

        _counters.Clear();
    }

    internal readonly record struct CounterInstance(string Name, double Value);

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct PdhFmtCounterValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public IntPtr Name;
        public PdhFmtCounterValue Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArrayW(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        out uint itemCount,
        IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);
}
