using System.Runtime.InteropServices;

namespace LoadLens.Core.Monitoring;

internal sealed record DxgiAdapterInfo(
    string Name,
    ulong Luid,
    double DedicatedMb,
    double SharedSystemMb,
    uint VendorId,
    uint DeviceId,
    uint Flags);

/// <summary>
/// Reads adapter names and physical VRAM size from DXGI.
/// Only four vtable slots are used, so the COM interfaces are called manually;
/// this keeps the app free of third-party dependencies.
/// </summary>
internal static class DxgiInfo
{
    private static readonly Guid IidIdxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    private const int EnumAdapters1Slot = 12;
    private const int GetDesc1Slot = 10;
    private const int ReleaseSlot = 2;
    private const uint AdapterFlagSoftware = 0x2;

    public static IReadOnlyList<DxgiAdapterInfo> ReadAdapters()
    {
        var result = new List<DxgiAdapterInfo>();
        var iid = IidIdxgiFactory1;

        var hr = CreateDXGIFactory1(ref iid, out var factory);
        if (hr < 0 || factory == IntPtr.Zero)
        {
            return result;
        }

        try
        {
            var factoryVtable = Marshal.ReadIntPtr(factory);
            var enumAdapters1 = GetDelegate<EnumAdapters1Delegate>(factoryVtable, EnumAdapters1Slot);

            for (uint index = 0; index < 16; index++)
            {
                var enumHr = enumAdapters1(factory, index, out var adapter);
                if (enumHr < 0 || adapter == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    var descPtr = Marshal.AllocHGlobal(512);
                    try
                    {
                        var adapterVtable = Marshal.ReadIntPtr(adapter);
                        var getDesc1 = GetDelegate<GetDesc1Delegate>(adapterVtable, GetDesc1Slot);
                        if (getDesc1(adapter, descPtr) < 0)
                        {
                            continue;
                        }

                        var desc = Marshal.PtrToStructure<AdapterDesc1>(descPtr);
                        if ((desc.Flags & AdapterFlagSoftware) != 0)
                        {
                            continue;
                        }

                        result.Add(new DxgiAdapterInfo(
                            CleanName(desc.Description),
                            ((ulong)(uint)desc.Luid.HighPart << 32) | desc.Luid.LowPart,
                            desc.DedicatedVideoMemory.ToUInt64() / (1024.0 * 1024.0),
                            desc.SharedSystemMemory.ToUInt64() / (1024.0 * 1024.0),
                            desc.VendorId,
                            desc.DeviceId,
                            desc.Flags));
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(descPtr);
                    }
                }
                finally
                {
                    ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            ReleaseComObject(factory);
        }

        return result;
    }

    private static T GetDelegate<T>(IntPtr vtable, int slot) where T : Delegate
    {
        var functionPtr = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(functionPtr);
    }

    private static void ReleaseComObject(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var vtable = Marshal.ReadIntPtr(ptr);
            var release = GetDelegate<ReleaseDelegate>(vtable, ReleaseSlot);
            release(ptr);
        }
        catch
        {
            // Releasing a COM object should never take the app down.
        }
    }

    private static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Видеоадаптер";
        }

        return name.Trim('\0', ' ');
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr self, uint adapter, out IntPtr output);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr self, IntPtr desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(IntPtr self);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public Luid Luid;
        public uint Flags;
    }

    [DllImport("dxgi.dll", PreserveSig = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);
}
