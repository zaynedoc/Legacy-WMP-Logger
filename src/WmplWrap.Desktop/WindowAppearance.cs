using System.Runtime.InteropServices;

namespace WmplWrap.Desktop;

internal static class WindowAppearance
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    public static void ApplyDarkTitleBar(nint windowHandle, bool useDarkTheme)
    {
        if (windowHandle == 0) return;

        var value = useDarkTheme ? 1 : 0;
        if (DwmSetWindowAttribute(windowHandle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            DwmSetWindowAttribute(windowHandle, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int valueSize);
}
