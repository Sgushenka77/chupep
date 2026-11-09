using System.Globalization;
using System.Text.RegularExpressions;
using LoadLens.Core.Models;

namespace LoadLens.Core.Monitoring;

internal sealed record GpuProcessUsage(double GpuPercent, double VramMb, double SharedMb);

internal sealed record GpuReading(
    GpuMetrics Metrics,
    IReadOnlyDictionary<int, GpuProcessUsage> Processes);

/// <summary>
/// Combines DXGI adapter metadata with PDH GPU counters. PDH gives live
/// utilization and memory usage globally and per process; DXGI provides the
/// physical VRAM size and adapter name.
/// </summary>
internal sealed class GpuMeter
{
    private const double BytesToMb = 1024.0 * 1024.0;

    private static readonly Regex PidLuidRegex = new(
        @"^pid_(?<pid>\d+)_luid_(?<hi>0x[0-9a-fA-F]+)_(?<lo>0x[0-9a-fA-F]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LuidRegex = new(
        @"^luid_(?<hi>0x[0-9a-fA-F]+)_(?<lo>0x[0-9a-fA-F]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<DxgiAdapterInfo> _adapters;

    public GpuMeter()
    {
        _adapters = DxgiInfo.ReadAdapters();
    }

    public IReadOnlyList<DxgiAdapterInfo> Adapters => _adapters;

    public GpuReading Read(PdhQuery pdh)
    {
        if (_adapters.Count == 0)
        {
            return new GpuReading(GpuMetrics.Unavailable(), new Dictionary<int, GpuProcessUsage>());
        }

        var processUsage = ReadProcessUsage(pdh);
        var adapterMemory = ReadAdapterMemory(pdh);
        var engineLoadByPid = new Dictionary<int, double>();
        var engineLoadByLuid = new Dictionary<ulong, double>();
        var engine3DByLuid = new Dictionary<ulong, double>();

        foreach (var instance in pdh.GetInstances(PdhQuery.GpuEngineUtilization))
        {
            var match = PidLuidRegex.Match(instance.Name);
            if (!match.Success)
            {
                continue;
            }

            var pid = int.Parse(match.Groups["pid"].Value, CultureInfo.InvariantCulture);
            if (pid <= 4)
            {
                continue;
            }

            var luid = ParseLuid(match);
            var value = Math.Clamp(instance.Value, 0, 100);
            engineLoadByPid[pid] = Math.Min(100, engineLoadByPid.GetValueOrDefault(pid) + value);
            engineLoadByLuid[luid] = Math.Min(100, engineLoadByLuid.GetValueOrDefault(luid) + value);

            if (IsThreeDEngine(instance.Name))
            {
                engine3DByLuid[luid] = Math.Min(100, engine3DByLuid.GetValueOrDefault(luid) + value);
            }
        }

        foreach (var pid in processUsage.Keys.ToArray())
        {
            if (engineLoadByPid.TryGetValue(pid, out var gpu))
            {
                processUsage[pid] = processUsage[pid] with { GpuPercent = gpu };
            }
        }

        var selected = SelectAdapter(adapterMemory, engine3DByLuid);
        var selectedMemory = adapterMemory.GetValueOrDefault(selected.Luid);
        var usedMb = selectedMemory.EffectiveDedicatedMb;
        var sharedMb = selectedMemory.SharedMb;

        if (usedMb <= 0)
        {
            usedMb = processUsage.Values.Sum(p => p.VramMb);
            sharedMb = processUsage.Values.Sum(p => p.SharedMb);
        }

        var totalMb = selected.DedicatedMb;
        var integrated = IsIntegrated(selected);
        double? usagePercent = totalMb > 0 && !integrated
            ? Math.Clamp(usedMb / totalMb * 100.0, 0, 100)
            : null;

        double loadPercent;
        if (engineLoadByLuid.Count > 0)
        {
            loadPercent = engineLoadByLuid.GetValueOrDefault(selected.Luid);
            if (loadPercent <= 0)
            {
                loadPercent = engineLoadByLuid.Values.DefaultIfEmpty(0).Max();
            }
        }
        else
        {
            loadPercent = engineLoadByPid.Values.DefaultIfEmpty(0).Sum();
        }

        var available = pdh.HasCounter(PdhQuery.GpuEngineUtilization);
        var metrics = new GpuMetrics(
            selected.Name,
            available,
            Math.Clamp(loadPercent, 0, 100),
            usedMb > 0 ? usedMb : null,
            totalMb > 0 ? totalMb : null,
            sharedMb > 0 ? sharedMb : null,
            usagePercent,
            selected.SharedSystemMb > 0 ? selected.SharedSystemMb : null,
            integrated);

        return new GpuReading(metrics, processUsage);
    }

    private DxgiAdapterInfo SelectAdapter(
        IReadOnlyDictionary<ulong, AdapterMemory> memory,
        IReadOnlyDictionary<ulong, double> load3DByLuid)
    {
        var discrete = _adapters
            .Where(a => !IsIntegrated(a))
            .OrderByDescending(a => a.DedicatedMb)
            .FirstOrDefault();

        if (discrete is not null)
        {
            // Only switch to an iGPU when it is clearly the one rendering a game.
            // Desktop composition can produce a few percent of 3D load on Intel,
            // and we do not want the dashboard to flap between adapters.
            var activeIntegrated = _adapters
                .Where(IsIntegrated)
                .Select(a => (Adapter: a, Load: load3DByLuid.GetValueOrDefault(a.Luid)))
                .OrderByDescending(x => x.Load)
                .FirstOrDefault();

            if (activeIntegrated.Adapter is not null &&
                activeIntegrated.Load >= 20 &&
                activeIntegrated.Load > load3DByLuid.GetValueOrDefault(discrete.Luid) + 10)
            {
                return activeIntegrated.Adapter;
            }

            return discrete;
        }

        var byLoad = _adapters
            .Select(a => (Adapter: a, Load: load3DByLuid.GetValueOrDefault(a.Luid)))
            .OrderByDescending(x => x.Load)
            .First();

        if (byLoad.Load > 0)
        {
            return byLoad.Adapter;
        }

        if (memory.Count > 0)
        {
            var byUsage = _adapters
                .Select(a => (Adapter: a, Usage: memory.GetValueOrDefault(a.Luid).EffectiveDedicatedMb))
                .OrderByDescending(x => x.Usage)
                .First();

            if (byUsage.Usage > 0)
            {
                return byUsage.Adapter;
            }
        }

        return _adapters.OrderByDescending(a => a.DedicatedMb).First();
    }

    private static bool IsThreeDEngine(string instanceName) =>
        instanceName.Contains("engtype_3d", StringComparison.OrdinalIgnoreCase) ||
        instanceName.Contains("engtype_compute", StringComparison.OrdinalIgnoreCase);

    private static bool IsIntegrated(DxgiAdapterInfo adapter) =>
        adapter.DedicatedMb < 1024 &&
        (adapter.VendorId == 0x8086 || (adapter.VendorId == 0x1002 && adapter.DedicatedMb < 512));

    private static Dictionary<int, GpuProcessUsage> ReadProcessUsage(PdhQuery pdh)
    {
        var local = new Dictionary<int, double>();
        var dedicated = new Dictionary<int, double>();
        var shared = new Dictionary<int, double>();

        ReadMemoryCounter(pdh, PdhQuery.GpuProcessLocal, local);
        ReadMemoryCounter(pdh, PdhQuery.GpuProcessDedicated, dedicated);
        ReadMemoryCounter(pdh, PdhQuery.GpuProcessShared, shared);

        var result = new Dictionary<int, GpuProcessUsage>();
        foreach (var pid in local.Keys.Concat(dedicated.Keys).Concat(shared.Keys).Distinct())
        {
            var localMb = local.GetValueOrDefault(pid);
            var dedicatedMb = dedicated.GetValueOrDefault(pid);
            result[pid] = new GpuProcessUsage(
                0,
                Math.Max(localMb, dedicatedMb),
                shared.GetValueOrDefault(pid));
        }

        return result;
    }

    private static Dictionary<ulong, AdapterMemory> ReadAdapterMemory(PdhQuery pdh)
    {
        var local = new Dictionary<ulong, double>();
        var dedicated = new Dictionary<ulong, double>();
        var shared = new Dictionary<ulong, double>();

        ReadLuidCounter(pdh, PdhQuery.GpuLocalAdapter, local);
        ReadLuidCounter(pdh, PdhQuery.GpuAdapterDedicated, dedicated);
        ReadLuidCounter(pdh, PdhQuery.GpuAdapterShared, shared);

        var result = new Dictionary<ulong, AdapterMemory>();
        foreach (var luid in local.Keys.Concat(dedicated.Keys).Concat(shared.Keys).Distinct())
        {
            result[luid] = new AdapterMemory(
                local.GetValueOrDefault(luid),
                dedicated.GetValueOrDefault(luid),
                shared.GetValueOrDefault(luid));
        }

        return result;
    }

    private static void ReadMemoryCounter(PdhQuery pdh, string path, Dictionary<int, double> target)
    {
        foreach (var instance in pdh.GetInstances(path))
        {
            var match = PidLuidRegex.Match(instance.Name);
            if (!match.Success)
            {
                continue;
            }

            var pid = int.Parse(match.Groups["pid"].Value, CultureInfo.InvariantCulture);
            if (pid <= 4)
            {
                continue;
            }

            target[pid] = target.GetValueOrDefault(pid) + Math.Max(0, instance.Value) / BytesToMb;
        }
    }

    private static void ReadLuidCounter(PdhQuery pdh, string path, Dictionary<ulong, double> target)
    {
        foreach (var instance in pdh.GetInstances(path))
        {
            var match = LuidRegex.Match(instance.Name);
            if (!match.Success)
            {
                continue;
            }

            var luid = ParseLuid(match);
            target[luid] = target.GetValueOrDefault(luid) + Math.Max(0, instance.Value) / BytesToMb;
        }
    }

    private static ulong ParseLuid(Match match)
    {
        var high = ulong.Parse(match.Groups["hi"].Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var low = ulong.Parse(match.Groups["lo"].Value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (high << 32) | low;
    }

    private readonly record struct AdapterMemory(double LocalMb, double DedicatedMb, double SharedMb)
    {
        public double EffectiveDedicatedMb => Math.Max(LocalMb, DedicatedMb);
    }
}
