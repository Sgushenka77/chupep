using LoadLens.Core.Models;

namespace LoadLens.Core.Monitoring;

internal static class MemoryMeter
{
    private const double BytesToMb = 1024.0 * 1024.0;

    public static MemoryMetrics Read(PdhQuery pdh)
    {
        var status = NativeMethods.MemoryStatusEx.Create();
        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            return new MemoryMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var totalMb = status.TotalPhys / BytesToMb;
        var availableMb = status.AvailPhys / BytesToMb;
        var usedPercent = totalMb > 0 ? (totalMb - availableMb) / totalMb * 100.0 : 0;

        var commitLimitMb = status.TotalPageFile / BytesToMb;
        var commitAvailableMb = status.AvailPageFile / BytesToMb;
        var commitUsedMb = Math.Max(0, commitLimitMb - commitAvailableMb);
        var commitPercent = commitLimitMb > 0 ? commitUsedMb / commitLimitMb * 100.0 : 0;

        return new MemoryMetrics(
            totalMb,
            availableMb,
            Math.Clamp(usedPercent, 0, 100),
            commitUsedMb,
            commitLimitMb,
            Math.Clamp(commitPercent, 0, 100),
            pdh.GetValue(PdhQuery.PagesPerSecond) ?? 0,
            pdh.GetValue(PdhQuery.PageFaults) ?? 0,
            pdh.GetValue(PdhQuery.PagesInput) ?? 0,
            pdh.GetValue(PdhQuery.PagingFile) ?? 0);
    }
}

internal static class DiskMeter
{
    public static DiskMetrics Read(PdhQuery pdh)
    {
        var active = pdh.GetValue(PdhQuery.DiskTime) ?? 0;
        var queue = pdh.GetValue(PdhQuery.DiskQueue) ?? 0;
        var latencySeconds = pdh.GetValue(PdhQuery.DiskLatency) ?? 0;
        var read = pdh.GetValue(PdhQuery.DiskRead) ?? 0;
        var write = pdh.GetValue(PdhQuery.DiskWrite) ?? 0;

        // Avg. Disk sec/Transfer is exposed in seconds by PDH.
        var latencyMs = latencySeconds * 1000.0;

        return new DiskMetrics(
            Math.Clamp(active, 0, 100),
            Math.Clamp(queue, 0, 1000),
            Math.Clamp(latencyMs, 0, 5000),
            Math.Max(0, read) / (1024.0 * 1024.0),
            Math.Max(0, write) / (1024.0 * 1024.0));
    }
}
