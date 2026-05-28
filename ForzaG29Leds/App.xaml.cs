using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;

namespace ForzaG29Leds;

public partial class App : Application
{
    private NotifyIcon? _tray;
    private TelemetryService? _service;
    private Settings _settings = Settings.Load();
    private SettingsWindow? _settingsWindow;

    private bool _g29Connected;
    private bool _telemetryActive;

    // Icons created once on the UI thread at startup and reused.
    private Icon _idleIcon = null!;
    private Icon _readyIcon = null!;
    private Icon _liveIcon = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _idleIcon = BuildIcon(Color.FromArgb(120, 120, 120));
        _readyIcon = BuildIcon(Color.FromArgb(80, 200, 80));
        _liveIcon = BuildIcon(Color.FromArgb(255, 220, 60));

        _service = new TelemetryService();
        _service.G29StatusChanged += OnG29Status;
        _service.TelemetryStatusChanged += OnTelemetryStatus;
        _service.Start(_settings);

        _tray = new NotifyIcon
        {
            Icon = _idleIcon,
            Visible = true,
            Text = "ForzaG29Leds",
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    // ── Tray icon state ───────────────────────────────────────────────────────

    private void OnG29Status(bool connected)
    {
        _g29Connected = connected;
        Dispatcher.Invoke(RefreshIcon);
    }

    private void OnTelemetryStatus(bool receiving)
    {
        _telemetryActive = receiving;
        Dispatcher.Invoke(RefreshIcon);
    }

    private void RefreshIcon()
    {
        if (_tray is null) return;

        Icon icon;
        string tip;

        if (!_g29Connected)
        {
            icon = _idleIcon;
            tip = "ForzaG29Leds — G29 not found";
        }
        else if (_telemetryActive)
        {
            icon = _liveIcon;
            tip = "ForzaG29Leds — Live";
        }
        else
        {
            icon = _readyIcon;
            tip = "ForzaG29Leds — G29 ready";
        }

        _tray.Icon = icon;
        _tray.Text = tip;
    }

    private static Icon BuildIcon(Color fill)
    {
        using var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(fill);
            g.FillEllipse(brush, 2, 2, 12, 12);
        }
        IntPtr hIcon = bmp.GetHicon();
        Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    // ── Tray menu ─────────────────────────────────────────────────────────────

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        return menu;
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _service!);
        _settingsWindow.SettingsSaved += s =>
        {
            _settings = s;
            _service?.ApplySettings(s);
        };
        _settingsWindow.Show();
    }

    private void ExitApp()
    {
        Shutdown(); // cleanup handled in OnExit
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _service?.Dispose();
        _idleIcon.Dispose();
        _readyIcon.Dispose();
        _liveIcon.Dispose();
        base.OnExit(e);
    }
}
