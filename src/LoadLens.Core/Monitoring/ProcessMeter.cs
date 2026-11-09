using System.Diagnostics;
using LoadLens.Core.Models;

namespace LoadLens.Core.Monitoring;

/// <summary>
/// Samples CPU per process and the busiest thread of every active process.
/// The thread data is what makes a "single-thread bottleneck" visible.
/// </summary>
internal sealed class ProcessMeter
{
    private const double BytesToMb = 1024.0 * 1024.0;

    private static readonly HashSet<string> SystemProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System", "smss", "csrss",
        "wininit", "services", "lsass", "winlogon", "fontdrvhost", "dwm", "svchost", "audiodg",
        "conhost", "wudfhost", "spoolsv"
    };

    private readonly Dictionary<int, ProcessBaseline> _processBaselines = new();
    private Dictionary<(int Pid, int ThreadId), TimeSpan> _threadBaselines = new();

    public void Reset()
    {
        _processBaselines.Clear();
        _threadBaselines = new Dictionary<(int Pid, int ThreadId), TimeSpan>();
    }

    public IReadOnlyList<ProcessMetrics> Read(
        double elapsedSeconds,
        int logicalCores,
        GpuReading gpuReading)
    {
        var elapsed = Math.Max(0.05, elapsedSeconds);
        var cores = Math.Max(1, logicalCores);
        var windowPids = ReadWindowProcessIds();
        var nextThreadBaselines = new Dictionary<(int Pid, int ThreadId), TimeSpan>();
        var result = new List<ProcessMetrics>(160);
        var alivePids = new HashSet<int>();

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return result;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                int pid;
                try
                {
                    pid = process.Id;
                }
                catch
                {
                    continue;
                }

                if (pid <= 0)
                {
                    continue;
                }

                alivePids.Add(pid);

                string name;
                try
                {
                    name = process.ProcessName;
                }
                catch
                {
                    name = "unknown";
                }

                var hasWindow = windowPids.Contains(pid);
                var gpu = gpuReading.Processes.TryGetValue(pid, out var gpuFound)
                    ? gpuFound
                    : new GpuProcessUsage(0, 0, 0);
                double workingSetMb = 0;
                double privateMb = 0;
                var threadCount = 0;

                try
                {
                    workingSetMb = process.WorkingSet64 / BytesToMb;
                    privateMb = process.PrivateMemorySize64 / BytesToMb;
                    threadCount = process.Threads.Count;
                }
                catch
                {
                    // Protected processes can deny access; their metrics stay at zero.
                }

                TimeSpan totalCpu;
                try
                {
                    totalCpu = process.TotalProcessorTime;
                }
                catch
                {
                    continue;
                }

                var previous = _processBaselines.GetValueOrDefault(pid);
                var processCpuSeconds = 0.0;

                if (previous is not null &&
                    totalCpu >= previous.TotalCpu &&
                    (DateTime.UtcNow - previous.TakenAt).TotalSeconds < 30)
                {
                    processCpuSeconds = (totalCpu - previous.TotalCpu).TotalSeconds;
                }

                _processBaselines[pid] = new ProcessBaseline(totalCpu, DateTime.UtcNow);
                var cpuPercent = Math.Clamp(processCpuSeconds / elapsed / cores * 100.0, 0, 100);

                var maxThreadPercent = 0.0;
                var hottestThreadShare = 0.0;

                if (threadCount > 0 &&
                    pid > 4 &&
                    (cpuPercent >= 0.08 || gpu.GpuPercent >= 0.25))
                {
                    var (maxThread, sumThreads) = ReadThreads(process, pid, elapsed, nextThreadBaselines);
                    maxThreadPercent = maxThread;
                    if (sumThreads > 0)
                    {
                        hottestThreadShare = Math.Clamp(maxThread / sumThreads, 0, 1);
                    }
                }

                var activity =
                    cpuPercent +
                    gpu.GpuPercent * 0.85 +
                    Math.Min(8, gpu.VramMb / 512.0);

                if (hasWindow)
                {
                    activity += 2;
                }

                if (SystemProcessNames.Contains(name))
                {
                    activity *= 0.05;
                }

                if (pid == Environment.ProcessId)
                {
                    activity = 0;
                }

                if (activity < 0.03 && gpu.VramMb < 8)
                {
                    continue;
                }

                result.Add(new ProcessMetrics(
                    pid,
                    name,
                    cpuPercent,
                    maxThreadPercent,
                    hottestThreadShare,
                    threadCount,
                    Math.Max(0, workingSetMb),
                    Math.Max(0, privateMb),
                    gpu.GpuPercent,
                    gpu.VramMb,
                    gpu.SharedMb,
                    hasWindow,
                    activity));
            }
        }

        _threadBaselines = nextThreadBaselines;

        foreach (var deadPid in _processBaselines.Keys.Where(pid => !alivePids.Contains(pid)).ToArray())
        {
            _processBaselines.Remove(deadPid);
        }

        return result
            .OrderByDescending(p => p.ActivityScore)
            .Take(80)
            .ToArray();
    }

    private (double MaxThreadPercent, double SumThreadsPercent) ReadThreads(
        Process process,
        int pid,
        double elapsed,
        Dictionary<(int Pid, int ThreadId), TimeSpan> nextBaselines)
    {
        var max = 0.0;
        var sum = 0.0;

        try
        {
            foreach (ProcessThread thread in process.Threads)
            {
                try
                {
                    var threadId = thread.Id;
                    var cpu = thread.TotalProcessorTime;
                    var key = (pid, threadId);

                    if (_threadBaselines.TryGetValue(key, out var previous) && cpu >= previous)
                    {
                        var corePercent = Math.Clamp(
                            (cpu - previous).TotalSeconds / elapsed * 100.0,
                            0,
                            100);
                        max = Math.Max(max, corePercent);
                        sum += corePercent;
                    }

                    nextBaselines[key] = cpu;
                }
                catch
                {
                    // A thread may exit between enumeration and reading its time.
                }
            }
        }
        catch
        {
            // The process may exit or be protected.
        }

        return (max, sum);
    }

    private static HashSet<int> ReadWindowProcessIds()
    {
        var result = new HashSet<int>();

        try
        {
            NativeMethods.EnumWindows((hWnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd) ||
                    NativeMethods.GetWindowTextLengthW(hWnd) == 0)
                {
                    return true;
                }

                NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
                if (pid > 4)
                {
                    result.Add((int)pid);
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Window enumeration is a nice-to-have, not a reason to fail a sample.
        }

        return result;
    }

    private sealed record ProcessBaseline(TimeSpan TotalCpu, DateTime TakenAt);
}
