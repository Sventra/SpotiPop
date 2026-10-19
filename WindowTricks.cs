using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SpotiPop;

static class WindowTricks
{
    const int GwlExStyle = -20;
    const int WsExToolWindow = 0x80;       // keep out of Alt+Tab
    const int WsExNoActivate = 0x08000000; // never steal focus from the app you're using
    const uint SwpNoSize = 0x1, SwpNoZOrder = 0x4, SwpNoActivate = 0x10;
    const int DwmUseImmersiveDarkMode = 20;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    public static void MakeOverlay(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowLong(hwnd, GwlExStyle, GetWindowLong(hwnd, GwlExStyle) | WsExToolWindow | WsExNoActivate);
    }

    /// <summary>Moves to a physical-pixel position (works across monitors, unlike Left/Top).</summary>
    public static void MoveTo(Window window, int x, int y)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint { public int X, Y; }

    public static bool TryGetCursor(out int x, out int y)
    {
        var ok = GetCursorPos(out var p);
        x = p.X;
        y = p.Y;
        return ok;
    }

    /// <summary>The screen with this device name, or the primary screen when unset or unplugged.</summary>
    public static System.Windows.Forms.Screen FindScreen(string? deviceName) =>
        System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName)
        ?? System.Windows.Forms.Screen.PrimaryScreen
        ?? System.Windows.Forms.Screen.AllScreens[0];

    public static void UseDarkTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var on = 1;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref on, sizeof(int));
    }
}
