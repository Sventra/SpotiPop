using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace SpotiPop;

/// <summary>
/// Hands unused memory back to Windows a few seconds after activity settles.
/// WPF and the media APIs leave a lot of transient pages resident; this keeps the idle footprint small.
/// </summary>
static class MemoryTrim
{
    [DllImport("psapi.dll")] static extern bool EmptyWorkingSet(IntPtr process);

    static readonly DispatcherTimer Timer = Create();

    static DispatcherTimer Create()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            using var self = Process.GetCurrentProcess();
            EmptyWorkingSet(self.Handle);
        };
        return timer;
    }

    /// <summary>Debounced: only runs once nothing has asked for a trim for a few seconds. Call on the UI thread.</summary>
    public static void Request()
    {
        Timer.Stop();
        Timer.Start();
    }
}
