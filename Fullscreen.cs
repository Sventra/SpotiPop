using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpotiPop;

/// <summary>Detects games, videos and presentations that own the whole screen, so overlays can stay out of the way.</summary>
static class Fullscreen
{
    const int GwlStyle = -16;
    const int WsCaption = 0x00C00000;
    const int QunsRunningD3dFullScreen = 3;
    const int QunsPresentationMode = 4;
    const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public int Flags; }

    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    public static bool IsActive()
    {
        // Exclusive-mode Direct3D games and presentations.
        if (SHQueryUserNotificationState(out var state) == 0
            && state is QunsRunningD3dFullScreen or QunsPresentationMode) return true;

        // Borderless-windowed games and fullscreen video: a captionless window covering its whole monitor.
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsIconic(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == OwnProcessId || IsShell(hwnd)) return false;
        if ((GetWindowLong(hwnd, GwlStyle) & WsCaption) == WsCaption) return false; // normal or maximized window

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetWindowRect(hwnd, out var window) || !GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info)) return false;
        return window.Left <= info.Monitor.Left && window.Top <= info.Monitor.Top
            && window.Right >= info.Monitor.Right && window.Bottom >= info.Monitor.Bottom;
    }

    /// <summary>The desktop itself and the taskbar span the screen too but are not fullscreen apps.</summary>
    static bool IsShell(IntPtr hwnd)
    {
        var name = new StringBuilder(64);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
}
