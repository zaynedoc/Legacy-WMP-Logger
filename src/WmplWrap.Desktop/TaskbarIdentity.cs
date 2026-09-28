using System.IO;
using System.Runtime.InteropServices;

namespace WmplWrap.Desktop;

internal static class TaskbarIdentity
{
    private const string AppUserModelId = "ZayneDockery.WmplWrap";
    private static readonly Guid PropertyStoreGuid = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly PropertyKey RelaunchIconResourceKey = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 3);
    private static readonly PropertyKey AppUserModelIdKey = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 5);

    public static void ConfigureProcess()
    {
        _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
    }

    public static void ConfigureWindow(IntPtr windowHandle)
    {
        var propertyStoreGuid = PropertyStoreGuid;
        if (windowHandle == IntPtr.Zero || SHGetPropertyStoreForWindow(windowHandle, ref propertyStoreGuid, out var propertyStore) < 0)
            return;

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "wmpl_recap_icon_bar.ico");
            if (!File.Exists(iconPath)) return;

            var iconResource = PropVariant.FromString($"{iconPath},0");
            var appId = PropVariant.FromString(AppUserModelId);
            try
            {
                var relaunchIconKey = RelaunchIconResourceKey;
                var appUserModelIdKey = AppUserModelIdKey;

                if (propertyStore.SetValue(ref relaunchIconKey, ref iconResource) < 0) return;
                if (propertyStore.SetValue(ref appUserModelIdKey, ref appId) < 0) return;

                _ = propertyStore.Commit();
            }
            finally
            {
                iconResource.Dispose();
                appId.Dispose();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(propertyStore);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(IntPtr windowHandle, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint propertyCount);
        int GetAt(uint propertyIndex, out PropertyKey key);
        int GetValue(ref PropertyKey key, out PropVariant value);
        int SetValue(ref PropertyKey key, ref PropVariant value);
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public PropertyKey(string formatId, uint propertyId)
        {
            FormatId = new Guid(formatId);
            PropertyId = propertyId;
        }

        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant : IDisposable
    {
        private const ushort VT_LPWSTR = 31;

        private ushort _valueType;
        private ushort _reserved1;
        private ushort _reserved2;
        private ushort _reserved3;
        private IntPtr _pointer;

        public static PropVariant FromString(string value) => new()
        {
            _valueType = VT_LPWSTR,
            _pointer = Marshal.StringToCoTaskMemUni(value)
        };

        public void Dispose()
        {
            if (_pointer == IntPtr.Zero) return;

            Marshal.FreeCoTaskMem(_pointer);
            _pointer = IntPtr.Zero;
        }
    }
}
