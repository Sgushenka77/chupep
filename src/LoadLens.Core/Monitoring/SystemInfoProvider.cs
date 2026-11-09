using System.Runtime.InteropServices;
using System.Security.Principal;
using LoadLens.Core.Models;
using Microsoft.Win32;

namespace LoadLens.Core.Monitoring;

internal static class SystemInfoProvider
{
    private const double BytesToMb = 1024.0 * 1024.0;

    public static SystemInfo Read(
        IReadOnlyList<DxgiAdapterInfo> gpuAdapters,
        string appVersion)
    {
        var logical = NativeMethods.GetActiveProcessorCount(NativeMethods.AllProcessorGroups);
        if (logical == 0)
        {
            logical = (ushort)Math.Max(1, Environment.ProcessorCount);
        }

        var physical = ReadPhysicalCoreCount();
        if (physical <= 0 || physical > logical)
        {
            physical = logical;
        }

        var (cpuName, baseMhz) = ReadCpuRegistryInfo();
        var memory = NativeMethods.MemoryStatusEx.Create();
        NativeMethods.GlobalMemoryStatusEx(ref memory);

        var gpu = gpuAdapters
            .OrderByDescending(a => a.DedicatedMb)
            .FirstOrDefault();

        return new SystemInfo(
            cpuName,
            physical,
            logical,
            baseMhz,
            gpu?.Name ?? "Видеоадаптер не найден",
            gpu?.DedicatedMb ?? 0,
            memory.TotalPhys / BytesToMb,
            GetOsName(),
            Environment.MachineName,
            appVersion,
            IsElevated());
    }

    private static (string Name, double BaseMhz) ReadCpuRegistryInfo()
    {
        const string fallback = "Процессор";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var name = key?.GetValue("ProcessorNameString") as string;
            var mhz = key?.GetValue("~MHz");
            var baseMhz = mhz switch
            {
                int i => i,
                long l => l,
                _ => 0
            };

            return (
                string.IsNullOrWhiteSpace(name) ? fallback : name.Trim(),
                Math.Max(0, baseMhz));
        }
        catch
        {
            return (fallback, 0);
        }
    }

    private static int ReadPhysicalCoreCount()
    {
        uint length = 0;
        NativeMethods.GetLogicalProcessorInformationEx(
            NativeMethods.RelationProcessorCore,
            IntPtr.Zero,
            ref length);

        if (length == 0)
        {
            return 0;
        }

        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!NativeMethods.GetLogicalProcessorInformationEx(
                    NativeMethods.RelationProcessorCore,
                    buffer,
                    ref length))
            {
                return 0;
            }

            var count = 0;
            var offset = 0;
            while (offset + 8 <= length)
            {
                var relationship = Marshal.ReadInt32(buffer, offset);
                var size = Marshal.ReadInt32(buffer, offset + 4);
                if (size <= 0)
                {
                    break;
                }

                if (relationship == NativeMethods.RelationProcessorCore)
                {
                    count++;
                }

                offset += size;
            }

            return count;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string GetOsName()
    {
        var version = Environment.OSVersion.Version;
        var name = version.Build >= 22000 ? "Windows 11" : "Windows 10";
        return $"{name} · сборка {version.Build}";
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
