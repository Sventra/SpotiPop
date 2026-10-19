using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SpotiPop;

public partial class App : Application
{
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValueName = "SpotiPop";

    Mutex? _singleInstance;
    Forms.NotifyIcon? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "SpotiPop.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        var settings = Settings.Load();
        var watcher = new SpotifyWatcher();
        var volume = new PlayerVolume(watcher);
        var popup = new PopupWindow(watcher, settings, volume);
        var island = new IslandWindow(watcher, settings, volume);
        _tray = CreateTray(popup, settings);

        try
        {
            await watcher.StartAsync();
            island.ApplySettings();
            MemoryTrim.Request();
        }
        catch (Exception ex)
        {
            Forms.MessageBox.Show($"Could not connect to Windows media controls:\n{ex.Message}", "SpotiPop");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    Forms.NotifyIcon CreateTray(PopupWindow popup, Settings settings)
    {
        SettingsWindow? settingsWindow = null;
        void OpenSettings()
        {
            if (settingsWindow is null)
            {
                settingsWindow = new SettingsWindow(settings);
                settingsWindow.Closed += (_, _) => settingsWindow = null;
                settingsWindow.Show();
            }
            settingsWindow.Activate();
        }

        var startup = new Forms.ToolStripMenuItem("Start with Windows") { Checked = IsAutoStart(), CheckOnClick = true };
        startup.CheckedChanged += (_, _) => SetAutoStart(startup.Checked);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show now playing", null, (_, _) => popup.Reveal());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());

        var tray = new Forms.NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "SpotiPop",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) popup.Reveal();
        };
        return tray;
    }

    static bool IsAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is not null;
    }

    static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;
        if (enabled) key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    /// <summary>The exe's own icon (app.ico), so the tray, Task Manager and Explorer all match.</summary>
    static Icon CreateIcon() =>
        Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
}
